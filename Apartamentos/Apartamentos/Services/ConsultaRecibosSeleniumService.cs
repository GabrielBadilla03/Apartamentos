using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
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
    private const string UrlIce = "https://agenciaelectricidad.cn.ice.go.cr/public/consultaFacturaElectrica?externo=true";
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
                "AyA requiere un CAPTCHA manual, pero la aplicacion se ejecuta sin escritorio interactivo. " +
                "Abra AyA mediante el enlace 'Abrir AyA' en su navegador y use el registro manual.");

        if (esAya && !visible)
            throw new InvalidOperationException(
                "AyA exige navegador visible para resolver el CAPTCHA. Configure Consulta:ChromeVisible=true.");

        bool headless = !visible || !sesionInteractiva;
        string perfil = Path.Combine(Path.GetTempPath(), "ApartamentosChrome_" + Guid.NewGuid().ToString("N"));
        ChromeDriver? driver = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var options = new ChromeOptions();
            options.AddArgument("--window-size=1400,900");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--user-data-dir=" + perfil);
            if (headless) options.AddArgument("--headless=new");

            // Permite usar Brave o una instalacion no estandar de Chrome.
            string? binary = config["Consulta:ChromeBinaryPath"];
            if (!string.IsNullOrWhiteSpace(binary))
            {
                if (!File.Exists(binary))
                    throw new InvalidOperationException("ChromeBinaryPath no existe: " + binary);
                options.BinaryLocation = binary;
            }

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            log.LogInformation("Iniciando Chrome {Modo} para {Proveedor} NIS {Nis}; sesion interactiva: {Interactiva}",
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
            try { if (Directory.Exists(perfil)) Directory.Delete(perfil, recursive: true); }
            catch (IOException) { /* Chrome puede continuar liberando archivos */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private ResultadoConsulta ConsultarAya(ChromeDriver driver, long nis, CancellationToken ct)
    {
        var input = Buscar(driver, 30, ct,
            By.XPath("//app-consultar-factura//input"),
            By.XPath("/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[1]/app-campo-formulario/div/input"));
        input.Clear();
        input.SendKeys(nis.ToString(CultureInfo.InvariantCulture));

        // Espera a que una persona resuelva el CAPTCHA en el escritorio del servidor.
        // No se simulan clics ni se intenta eludir la proteccion.
        bool hayCaptcha = driver.FindElements(By.CssSelector("iframe[src*='recaptcha'],iframe[title*='reCAPTCHA']")).Count > 0;
        if (hayCaptcha)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(150))
            { PollingInterval = TimeSpan.FromMilliseconds(500) };
            bool resuelto;
            try
            {
                resuelto = wait.Until(d => {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // Algunas implementaciones llenan el textarea en el documento principal.
                        var token = ((IJavaScriptExecutor)d).ExecuteScript(
                            "return document.querySelector('textarea[name=\"g-recaptcha-response\"]')?.value || ''; ") as string;
                        if (!string.IsNullOrWhiteSpace(token)) return true;
                        var frame = d.FindElements(By.CssSelector("iframe[src*='recaptcha'],iframe[title*='reCAPTCHA']")).FirstOrDefault();
                        if (frame is null) return false;
                        d.SwitchTo().Frame(frame);
                        try
                        {
                            var anchor = d.FindElements(By.Id("recaptcha-anchor")).FirstOrDefault();
                            return anchor?.GetAttribute("aria-checked") == "true";
                        }
                        finally { d.SwitchTo().DefaultContent(); }
                    }
                    catch (WebDriverException) { d.SwitchTo().DefaultContent(); return false; }
                });
            }
            catch (WebDriverTimeoutException ex)
            {
                throw new InvalidOperationException(
                    "No se resolvio el CAPTCHA de AyA dentro del tiempo permitido. " +
                    "Chrome debe estar visible en el escritorio del equipo que ejecuta ASP.NET.", ex);
            }
            if (!resuelto) throw new InvalidOperationException("AyA no confirmo que el CAPTCHA este resuelto.");
        }

        var consultar = Buscar(driver, 25, ct,
            By.XPath("//app-consultar-factura//button[contains(., 'Consultar')]"),
            By.XPath("/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[2]/button"));
        consultar.Click();

        // Columnas obtenidas del proyecto Java; se evitan IDs generados dinamicamente por el sitio.
        var monto = Buscar(driver, 35, ct,
            By.XPath("//ag-grid-angular//div[contains(@class,'ag-center-cols-container')]//div[contains(@class,'ag-row')]//div[contains(@class,'ag-cell')][6]//span"),
            By.XPath("//ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[6]/div/span"));
        var fecha = BuscarOpcional(driver, 6, ct,
            By.XPath("//ag-grid-angular//div[contains(@class,'ag-center-cols-container')]//div[contains(@class,'ag-row')]//div[contains(@class,'ag-cell')][3]//span"),
            By.XPath("//ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[3]/div/span"));
        return new ResultadoConsulta(ParseMonto(monto.Text), ParseFecha(fecha?.Text));
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
