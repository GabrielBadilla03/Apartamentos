using System.Globalization;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace Apartamentos.Services;

/// <summary>
/// Consultas asistidas heredadas. Selenium se ejecuta en el servidor;
/// el CAPTCHA del AyA requiere que una persona use la sesión gráfica del servidor.
/// Revise los XPath, ya que dependen de páginas de terceros.
/// </summary>
public sealed class ConsultaRecibosSeleniumService(IConfiguration config, ILogger<ConsultaRecibosSeleniumService> log) : IConsultaRecibosService
{
    private const string UrlAya="https://www.aya.go.cr/inicio/gestiones_interes/cf/consultar_factura";
    private const string UrlIce="https://agenciaelectricidad.cn.ice.go.cr/public/consultaFacturaElectrica";
    public Task<ResultadoConsulta> ConsultarAsync(string proveedor,long numeroServicio,CancellationToken ct)
    {
        if (!config.GetValue<bool>("Consulta:HabilitarSelenium"))
            throw new InvalidOperationException("Consultas externas deshabilitadas. Habilite Selenium cuando el navegador y los selectores estén verificados.");
        if(proveedor is not ("agua" or "luz")) throw new ArgumentException("Proveedor inválido");
        return Task.Run(()=>ConsultarEnNavegador(proveedor,numeroServicio,ct),ct);
    }
    private ResultadoConsulta ConsultarEnNavegador(string proveedor,long id,CancellationToken ct)
    {
        bool aya=proveedor=="agua";
        var options=new ChromeOptions();
        options.AddArgument("--window-size=1400,900");
        if(!config.GetValue("Consulta:ChromeVisible",true) && !aya) options.AddArgument("--headless=new");
        // AyA nunca se inicia headless: requiere resolver reCAPTCHA de forma manual.
        using var driver=new ChromeDriver(options);
        try
        {
            driver.Navigate().GoToUrl(aya?UrlAya:UrlIce);
            IWebElement Find(string xpath,int seconds=25)=>new WebDriverWait(driver,TimeSpan.FromSeconds(seconds)).Until(d => {
                ct.ThrowIfCancellationRequested();
                var el=d.FindElement(By.XPath(xpath)); return el.Displayed ? el : null;
            });
            if(aya)
            {
                var nis=Find("/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[1]/app-campo-formulario/div/input");
                nis.Clear();nis.SendKeys(id.ToString(CultureInfo.InvariantCulture));
                new WebDriverWait(driver,TimeSpan.FromSeconds(120)).Until(d => {
                    ct.ThrowIfCancellationRequested();
                    d.SwitchTo().DefaultContent();
                    var frame=d.FindElements(By.XPath("//iframe[contains(@src,'recaptcha') or contains(@title,'reCAPTCHA')]")).FirstOrDefault();
                    if(frame is null) return false;
                    d.SwitchTo().Frame(frame);
                    var anchor=d.FindElements(By.Id("recaptcha-anchor")).FirstOrDefault();
                    bool done=anchor?.GetAttribute("aria-checked")=="true";
                    d.SwitchTo().DefaultContent();return done;
                });
                Task.Delay(3000,ct).GetAwaiter().GetResult();
                Find("/html/body/app-root/app-home/div/main/app-consultar-factura/div/div/form/div/div[2]/button").Click();
                var monto=Find("//ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[6]/div/span");
                var fecha=Find("//ag-grid-angular/div/div[1]/div[2]/div[3]/div[1]/div[2]/div/div/div[3]/div/span");
                return new ResultadoConsulta(LeerMonto(monto.Text),LeerFecha(fecha.Text));
            }
            else
            {
                var nis=Find("/html/body/div/div/main/div[2]/div/div/div/form/div[3]/div/input");
                nis.Clear();nis.SendKeys(id.ToString(CultureInfo.InvariantCulture));
                Find("/html/body/div/div/main/div[2]/div/div/div/form/div[4]/button").Click();
                var monto=Find("/html/body/div/div/main/div[2]/div/div/div/div/div[1]/div[3]/table/tbody/tr/td[4]/div[2]");
                var fecha=Find("/html/body/div/div/main/div[2]/div/div/div/div/div[1]/div[3]/table/tbody/tr/td[5]/div[2]/span");
                return new ResultadoConsulta(LeerMonto(monto.Text),LeerFecha(fecha.Text));
            }
        }
        catch(Exception ex) { log.LogError(ex,"Consulta {Proveedor} de servicio {Id} falló",proveedor,id);throw; }
    }
    private static DateTime? LeerFecha(string input)
    {
        var formats=new[]{"dd/MM/yyyy","yyyy-MM-dd","dd-MM-yyyy"};
        return DateTime.TryParseExact(input.Trim(),formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    }
    private static decimal LeerMonto(string input)
    {
        // El código antiguo guardaba el valor entero, eliminando decimales.
        var s=Regex.Replace(input,@"[^0-9,.]","");
        if(string.IsNullOrWhiteSpace(s)) throw new FormatException("No se encontró monto válido.");
        var lastDot=s.LastIndexOf('.');var lastComma=s.LastIndexOf(',');
        if(lastDot>=0 && lastComma>=0) s=lastDot>lastComma?s.Replace(",",""):s.Replace(".","").Replace(',', '.');
        else if(lastComma>=0) s=s.Length-lastComma-1==2?s.Replace(',', '.'):s.Replace(",","");
        else if(lastDot>=0 && s.Length-lastDot-1!=2) s=s.Replace(".","");
        if(!decimal.TryParse(s,NumberStyles.Number,CultureInfo.InvariantCulture,out var result))throw new FormatException("Formato de monto no reconocido.");
        return result;
    }
}
