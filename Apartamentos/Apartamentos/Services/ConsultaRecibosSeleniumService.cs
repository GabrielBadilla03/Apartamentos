using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace Apartamentos.Services;

/// <summary>
/// Consulta los portales publicos de AyA e ICE desde el equipo donde corre ASP.NET Core.
/// No resuelve ni omite reCAPTCHA. Si se usa IIS/Windows Service en Session 0,
/// el navegador no puede aparecer en la pantalla del usuario remoto.
/// Los selectores dependen del HTML de terceros y pueden requerir ajustes.
/// </summary>
public sealed class ConsultaRecibosSeleniumService(
    IConfiguration config,
    ILogger<ConsultaRecibosSeleniumService> log) : IConsultaRecibosService
{
    private const string UrlAya = "https://www.aya.go.cr/inicio/gestiones_interes/cf/consultar_factura";
    private const string UrlIce = "https://agenciaelectricidad.cn.ice.go.cr/public/consultaFacturaElectrica";
    private static readonly SemaphoreSlim NavegadorEnUso = new(1, 1);

    public async Task<ResultadoConsulta> ConsultarAsync(string proveedor, long numeroServicio, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Consulta:HabilitarSelenium"))
            throw new InvalidOperationException("La consulta automatica esta desactivada en la configuracion efectiva.");
        if (proveedor is not ("agua" or "luz") || numeroServicio <= 0)
            throw new ArgumentException("Proveedor o numero de servicio no valido.");

        // Evita que dos solicitudes abran Chrome simultaneamente y consuman muchos recursos.
        if (!await NavegadorEnUso.WaitAsync(TimeSpan.FromSeconds(8), ct))
            throw new InvalidOperationException("Ya se esta consultando otro servicio. Repita la operacion cuando termine.");

        try
        {
            return await Task.Run(() => ConsultarEnChrome(proveedor, numeroServicio, ct), ct);
        }
        finally
        {
            NavegadorEnUso.Release();
        }
    }

    public async Task<IReadOnlyList<ResultadoConsultaLote>> ConsultarLoteAsync(
        string proveedor, IReadOnlyCollection<long> numerosServicio, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Consulta:HabilitarSelenium"))
            throw new InvalidOperationException("La consulta automatica esta desactivada en la configuracion efectiva.");
        if (proveedor is not ("agua" or "luz"))
            throw new ArgumentException("Proveedor no valido.", nameof(proveedor));
        if (numerosServicio.Any(n => n <= 0))
            throw new ArgumentException("Hay numeros de servicio no validos.", nameof(numerosServicio));

        if (!await NavegadorEnUso.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new InvalidOperationException("Ya se esta consultando otro servicio. Intente cuando termine.");

        try
        {
            return await Task.Run(() => ConsultarLoteEnChrome(proveedor, numerosServicio, ct), ct);
        }
        finally
        {
            NavegadorEnUso.Release();
        }
    }

    // Permite que el PDF reciba y guarde cada monto en cuanto termina
    // la consulta, sin esperar a que todos los NIS se procesen.
    public async IAsyncEnumerable<ResultadoConsultaLote> ConsultarLoteProgresivoAsync(
        string proveedor, IReadOnlyCollection<long> numerosServicio,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!config.GetValue<bool>("Consulta:HabilitarSelenium"))
            throw new InvalidOperationException("La consulta automatica esta desactivada.");
        if (proveedor is not ("agua" or "luz") || numerosServicio.Any(n => n <= 0))
            throw new ArgumentException("Proveedor o numeros de servicio no validos.");

        var canal = Channel.CreateUnbounded<ResultadoConsultaLote>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var trabajador = Task.Run(async () =>
        {
            bool adquirido = false;
            try
            {
                adquirido = await NavegadorEnUso.WaitAsync(TimeSpan.FromSeconds(30), cancelar.Token);
                if (!adquirido)
                    throw new InvalidOperationException("Ya se esta consultando otro servicio.");

                ConsultarLoteEnChrome(proveedor, numerosServicio, cancelar.Token,
                    item => canal.Writer.TryWrite(item));
                canal.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                canal.Writer.TryComplete(ex);
            }
            finally
            {
                if (adquirido) NavegadorEnUso.Release();
            }
        });

        try
        {
            await foreach (var item in canal.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            cancelar.Cancel();
            await trabajador;
        }
    }

    private IReadOnlyList<ResultadoConsultaLote> ConsultarLoteEnChrome(
        string proveedor, IReadOnlyCollection<long> numerosServicio, CancellationToken ct,
        Action<ResultadoConsultaLote>? alResultado = null)
    {
        bool esAya = proveedor == "agua";
        bool visible = config.GetValue("Consulta:ChromeVisible", true);
        bool sesionInteractiva = Environment.UserInteractive;
        if (OperatingSystem.IsWindows())
            sesionInteractiva = sesionInteractiva && Process.GetCurrentProcess().SessionId != 0;

        if (esAya && (!visible || !sesionInteractiva))
            throw new InvalidOperationException(
                "AyA necesita una ventana Brave visible para resolver manualmente el CAPTCHA. " +
                "Ejecute la aplicacion en una sesion de escritorio interactiva.");

        bool headless = !visible || !sesionInteractiva;
        string perfilTemporal = Path.Combine(Path.GetTempPath(),
            (esAya ? "ApartamentosBraveLote_" : "ApartamentosChromeLote_") + Guid.NewGuid().ToString("N"));
        ChromeDriver? driver = null;
        var resultados = new List<ResultadoConsultaLote>();

        try
        {
            ct.ThrowIfCancellationRequested();
            var options = new ChromeOptions();
            options.AddArgument("--window-size=1400,900");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--user-data-dir=" + perfilTemporal);
            if (headless) options.AddArgument("--headless=new");

            ConfigurarBinarioNavegador(options, esAya);

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(50));
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(35);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

            // AyA conserva la misma pagina durante todo el lote.
            // ICE mantiene el comportamiento que ya funcionaba.
            if (esAya)
                driver.Navigate().GoToUrl(UrlAya);

            foreach (long numero in numerosServicio.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Con AyA reemplazar el NIS sin volver a cargar la pagina.
                    if (!esAya)
                        driver.Navigate().GoToUrl(UrlIce);
                    var resultado = esAya
                        ? ConsultarAya(driver, numero, ct)
                        : ConsultarIce(driver, numero, ct);
                    var item = new ResultadoConsultaLote(numero, resultado, null);
                    resultados.Add(item);
                    alResultado?.Invoke(item);
                    log.LogInformation("Fecha consultada: {Proveedor} NIS {Nis}: {Fecha}",
                        proveedor, numero, resultado.FechaVencimiento);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Fallo la consulta de fecha de {Proveedor} NIS {Nis}",
                        proveedor, numero);
                    var item = new ResultadoConsultaLote(numero, null, ex.Message);
                    resultados.Add(item);
                    alResultado?.Invoke(item);
                }
            }
            return resultados;
        }
        finally
        {
            if (driver is not null)
            {
                try { driver.Quit(); }
                catch (Exception ex) { log.LogWarning(ex, "No se pudo cerrar Chrome del lote"); }
                try { driver.Dispose(); }
                catch (Exception ex) { log.LogWarning(ex, "No se pudo liberar Chrome del lote"); }
            }
            try { if (Directory.Exists(perfilTemporal)) Directory.Delete(perfilTemporal, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // AyA usa Brave por defecto. ICE conserva la configuracion de Chrome.
    private void ConfigurarBinarioNavegador(ChromeOptions options, bool esAya)
    {
        if (!esAya)
        {
            string? chrome = config["Consulta:ChromeBinaryPath"];
            if (!string.IsNullOrWhiteSpace(chrome))
            {
                if (!File.Exists(chrome))
                    throw new InvalidOperationException("Consulta:ChromeBinaryPath no existe: " + chrome);
                options.BinaryLocation = chrome;
            }
            return;
        }

        string? braveConfigurado = config["Consulta:BraveBinaryPath"];
        if (!string.IsNullOrWhiteSpace(braveConfigurado))
        {
            if (!File.Exists(braveConfigurado))
                throw new InvalidOperationException(
                    "Consulta:BraveBinaryPath no existe: " + braveConfigurado);
            options.BinaryLocation = braveConfigurado;
            return;
        }

        string[] rutasBrave =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
        };

        string? brave = rutasBrave.FirstOrDefault(File.Exists);
        if (brave is null)
            throw new InvalidOperationException(
                "No se encontro Brave para las consultas de AyA. Instale Brave o configure " +
                "Consulta:BraveBinaryPath con la ruta completa a brave.exe.");

        options.BinaryLocation = brave;
        log.LogInformation("AyA se consultara mediante Brave: {Ruta}", brave);
    }

    private ResultadoConsulta ConsultarEnChrome(string proveedor, long nis, CancellationToken ct)
    {
        bool esAya = proveedor == "agua";
        bool visible = config.GetValue("Consulta:ChromeVisible", true);
        bool sesionInteractiva = Environment.UserInteractive;
        if (OperatingSystem.IsWindows())
            sesionInteractiva = sesionInteractiva && Process.GetCurrentProcess().SessionId != 0;

        // El CAPTCHA de AyA debe resolverlo una persona con acceso al Chrome del servidor.
        if (esAya && !sesionInteractiva)
            throw new InvalidOperationException(
                "AyA requiere un CAPTCHA manual en Brave, pero la aplicacion se ejecuta sin escritorio interactivo. " +
                "Abra AyA mediante el enlace 'Abrir AyA' en su navegador y use el registro manual.");

        if (esAya && !visible)
            throw new InvalidOperationException(
                "AyA exige Brave visible para resolver el CAPTCHA. Configure Consulta:ChromeVisible=true.");

        bool headless = !visible || !sesionInteractiva;
        string perfilTemporal = Path.Combine(Path.GetTempPath(),
            (esAya ? "ApartamentosBrave_" : "ApartamentosChrome_") + Guid.NewGuid().ToString("N"));
        ChromeDriver? driver = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var options = new ChromeOptions();
            options.AddArgument("--window-size=1400,900");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--user-data-dir=" + perfilTemporal);
            if (headless) options.AddArgument("--headless=new");

            ConfigurarBinarioNavegador(options, esAya);

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            log.LogInformation("Iniciando navegador {Modo} para {Proveedor} NIS {Nis}; sesion interactiva: {Interactiva}",
                headless ? "sin ventana" : "visible", proveedor, nis, sesionInteractiva);
            // Un limite por comando impide que Selenium quede bloqueado de forma indefinida.
            driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(50));
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(35);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
            driver.Navigate().GoToUrl(esAya ? UrlAya : UrlIce);
            ct.ThrowIfCancellationRequested();

            var resultado = esAya ? ConsultarAya(driver, nis, ct) : ConsultarIce(driver, nis, ct);
            log.LogInformation("Consulta OK: {Proveedor} NIS {Nis}; monto {Monto}; vencimiento {Fecha}",
                proveedor, nis, resultado.Monto, resultado.FechaVencimiento);
            return resultado;
        }
        catch (WebDriverTimeoutException ex)
        {
            log.LogWarning(ex, "La pagina {Proveedor} no respondio para {Nis}", proveedor, nis);
            throw new InvalidOperationException(
                "El sitio del proveedor tardo demasiado en responder, no se encontro el resultado o cambio su estructura.", ex);
        }
        catch (NoSuchElementException ex)
        {
            log.LogWarning(ex, "Cambios de selectores en {Proveedor}, NIS {Nis}", proveedor, nis);
            throw new InvalidOperationException(
                "No se encontraron los campos esperados. El proveedor pudo modificar su pagina.", ex);
        }
        catch (WebDriverException ex)
        {
            if (esAya && (ex.Message.Contains("user data directory is already in use", StringComparison.OrdinalIgnoreCase) ||
                          ex.Message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "No se pudo iniciar Brave con el perfil temporal creado para AyA. Revise la version de Brave/ChromeDriver y los permisos de la carpeta temporal.", ex);
            log.LogError(ex, "No se pudo operar Chrome para {Proveedor} NIS {Nis}", proveedor, nis);
            throw new InvalidOperationException(
                "ChromeDriver fallo. Verifique Chrome/Brave, Selenium Manager, permisos del perfil y la sesion de Windows. " +
                "Detalle: " + ex.Message, ex);
        }
        finally
        {
            if (driver is not null)
            {
                try { driver.Quit(); }
                catch (Exception ex) { log.LogWarning(ex, "No se pudo cerrar ChromeDriver normalmente"); }
                try { driver.Dispose(); }
                catch (Exception ex) { log.LogWarning(ex, "No se pudo liberar ChromeDriver normalmente"); }
            }
            try { if (Directory.Exists(perfilTemporal)) Directory.Delete(perfilTemporal, recursive: true); }
            catch (IOException) { /* Chrome puede continuar liberando archivos */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private ResultadoConsulta ConsultarAya(ChromeDriver driver, long nis, CancellationToken ct)
    {
        // XPath verificados en la pagina de AyA: noviembre de 2026.
        const string inputXpath =
            "/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[1]/app-campo-formulario/div/input";
        const string consultarXpath =
            "/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[2]/button";
        const string vencimientoXpath =
            "/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/app-grid-table/div/ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[3]/div/span";
        const string montoXpath =
            "/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/app-grid-table/div/ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[6]/div/span";

        var input = Buscar(driver, 30, ct,
            By.XPath(inputXpath),
            By.XPath("//app-consultar-factura//input"));
        input.Clear();
        input.SendKeys(nis.ToString(CultureInfo.InvariantCulture));

        // reCAPTCHA se resuelve manualmente en Brave/Chrome. El checkbox
        // #recaptcha-anchor esta DENTRO del iframe, no en el documento principal.
        EsperarVerificacionAya(driver, ct);
        EsperarConCancelacion(TimeSpan.FromSeconds(3), ct);

        var consultar = Buscar(driver, 25, ct,
            By.XPath(consultarXpath),
            By.XPath("//app-consultar-factura//button[contains(., 'Consultar')]"));
        consultar.Click();

        // Usar primero las rutas exactas de la tabla actual. Los XPath
        // genericos quedan como respaldo ante cambios menores de estructura.
        var monto = Buscar(driver, 35, ct,
            By.XPath(montoXpath),
            By.XPath("//app-grid-table//ag-grid-angular//div[contains(@class,'ag-center-cols-container')]//div[contains(@class,'ag-row')]//div[contains(@class,'ag-cell')][6]//span"));
        var fecha = BuscarOpcional(driver, 12, ct,
            By.XPath(vencimientoXpath),
            By.XPath("//app-grid-table//ag-grid-angular//div[contains(@class,'ag-center-cols-container')]//div[contains(@class,'ag-row')]//div[contains(@class,'ag-cell')][3]//span"));

        // Ejemplos recibidos: fecha 20/11/2026, monto "¢ 3 461.00".
        // ParseFecha usa dd/MM/yyyy y ParseMonto quita moneda/espacios.
        return new ResultadoConsulta(ParseMonto(monto.Text), ParseFecha(fecha?.Text));
    }

    private void EsperarVerificacionAya(IWebDriver driver, CancellationToken ct)
    {
        const string selectorIframe = "iframe[src*='recaptcha'],iframe[title*='reCAPTCHA']";

        // El iframe de reCAPTCHA puede aparecer después de introducir el NIS.
        var esperaIframe = new WebDriverWait(driver, TimeSpan.FromSeconds(15))
        { PollingInterval = TimeSpan.FromMilliseconds(500) };
        IWebElement? iframe;
        try
        {
            iframe = esperaIframe.Until(d =>
            {
                ct.ThrowIfCancellationRequested();
                var encontrados = d.FindElements(By.CssSelector(selectorIframe));
                return encontrados.FirstOrDefault(x => x.GetAttribute("src")?.Contains("/anchor") == true)
                    ?? encontrados.FirstOrDefault();
            });
        }
        catch (WebDriverTimeoutException)
        {
            // El sitio puede no mostrar CAPTCHA en determinadas circunstancias.
            return;
        }

        // Intentar un clic normal de Selenium en la casilla (no resolver retos de Google).
        // El checkbox #recaptcha-anchor está dentro del iframe del widget.
        try
        {
            driver.SwitchTo().Frame(iframe);
            var esperaCasilla = new WebDriverWait(driver, TimeSpan.FromSeconds(12))
            { PollingInterval = TimeSpan.FromMilliseconds(500) };
            var casilla = esperaCasilla.Until(d =>
            {
                ct.ThrowIfCancellationRequested();
                return d.FindElements(By.Id("recaptcha-anchor"))
                    .FirstOrDefault(x => x.Displayed && x.Enabled);
            });
            if (casilla?.GetAttribute("aria-checked") != "true")
            {
                casilla?.Click();
                log.LogInformation("Se intento marcar automaticamente la casilla de reCAPTCHA de AyA.");
            }
        }
        catch (WebDriverException ex)
        {
            // Si el CAPTCHA impide la interacción automatizada, se puede
            // completar en la ventana visible sin detener inmediatamente la consulta.
            log.LogInformation(ex, "AyA no acepto el clic automatico; se esperara validacion manual.");
        }
        finally
        {
            driver.SwitchTo().DefaultContent();
        }

        // Esperar a que Google confirme la verificación. Si solicita imágenes u
        // otro desafío, una persona debe completarlo desde el navegador.
        var esperaVerificacion = new WebDriverWait(driver, TimeSpan.FromSeconds(150))
        { PollingInterval = TimeSpan.FromMilliseconds(500) };
        try
        {
            esperaVerificacion.Until(d =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var token = ((IJavaScriptExecutor)d).ExecuteScript(
                        "return document.querySelector('textarea[name=\"g-recaptcha-response\"]')?.value || '';") as string;
                    if (!string.IsNullOrWhiteSpace(token)) return true;

                    var frame = d.FindElements(By.CssSelector(selectorIframe))
                        .FirstOrDefault(x => x.GetAttribute("src")?.Contains("/anchor") == true)
                        ?? d.FindElements(By.CssSelector(selectorIframe)).FirstOrDefault();
                    if (frame is null) return false;

                    d.SwitchTo().Frame(frame);
                    try
                    {
                        var anchor = d.FindElements(By.Id("recaptcha-anchor")).FirstOrDefault();
                        return anchor?.GetAttribute("aria-checked") == "true";
                    }
                    finally
                    {
                        d.SwitchTo().DefaultContent();
                    }
                }
                catch (WebDriverException)
                {
                    d.SwitchTo().DefaultContent();
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new InvalidOperationException(
                "No se completo el reCAPTCHA de AyA. Si Google solicita un desafio adicional, " +
                "resuelvalo manualmente en el navegador visible.", ex);
        }
    }

    private ResultadoConsulta ConsultarIce(ChromeDriver driver, long nis, CancellationToken ct)
    {
        var input = Buscar(driver, 30, ct,
            By.CssSelector("input[placeholder*='NISE'],input[placeholder*='Nise'],input[placeholder*='nise']"),
            By.XPath("/html/body/div/div/main/div[2]/div/div/div/form/div[3]/div/input"));
        input.Clear();
        input.SendKeys(nis.ToString(CultureInfo.InvariantCulture));
        var button = Buscar(driver, 25, ct,
            By.XPath("//form//button[contains(.,'Consultar')]"),
            By.XPath("/html/body/div/div/main/div[2]/div/div/div/form/div[4]/button"));
        button.Click();

        var monto = Buscar(driver, 35, ct,
            By.XPath("//table//tbody/tr[1]/td[4]//div[2]"),
            By.XPath("/html/body/div/div/main/div[2]/div/div/div/div/div[1]/div[3]/table/tbody/tr/td[4]/div[2]"));
        var fecha = BuscarOpcional(driver, 6, ct,
            By.XPath("//table//tbody/tr[1]/td[5]//span"),
            By.XPath("/html/body/div/div/main/div[2]/div/div/div/div/div[1]/div[3]/table/tbody/tr/td[5]/div[2]/span"));
        return new ResultadoConsulta(ParseMonto(monto.Text), ParseFecha(fecha?.Text));
    }

    private static void EsperarConCancelacion(TimeSpan tiempo, CancellationToken ct)
    {
        Task.Delay(tiempo, ct).GetAwaiter().GetResult();
    }

    private static IWebElement Buscar(IWebDriver driver, int segundos, CancellationToken ct, params By[] selectores)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(segundos))
        { PollingInterval = TimeSpan.FromMilliseconds(500) };
        return wait.Until(d => {
            ct.ThrowIfCancellationRequested();
            foreach (var selector in selectores)
            {
                try
                {
                    var el = d.FindElements(selector).FirstOrDefault(x => x.Displayed && x.Enabled);
                    if (el is not null) return el;
                }
                catch (StaleElementReferenceException) { }
            }
            return null;
        })!;
    }

    private static IWebElement? BuscarOpcional(IWebDriver driver, int segundos, CancellationToken ct, params By[] selectores)
    {
        try { return Buscar(driver, segundos, ct, selectores); }
        catch (WebDriverTimeoutException) { return null; }
    }

    private static DateTime? ParseFecha(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var match = Regex.Match(input, @"\d{1,4}[-/]\d{1,2}[-/]\d{2,4}");
        if (!match.Success) return null;
        string[] formatos = { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy" };
        return DateTime.TryParseExact(match.Value, formatos, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result) ? result.Date : null;
    }

    private static decimal ParseMonto(string raw)
    {
        string s = Regex.Replace(raw ?? "", @"[^0-9,.]", "");
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("No se encontro un monto numerico en la respuesta del proveedor.");
        int punto = s.LastIndexOf('.');
        int coma = s.LastIndexOf(',');
        if (punto >= 0 && coma >= 0)
            s = punto > coma ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        else if (coma >= 0)
            s = s.Length - coma - 1 == 2 ? s.Replace(',', '.') : s.Replace(",", "");
        else if (punto >= 0 && s.Length - punto - 1 != 2)
            s = s.Replace(".", "");
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var monto)
            || monto < 0 || monto > 1000000000m)
            throw new FormatException("Monto del recibo invalido: " + raw);
        return monto;
    }
}
