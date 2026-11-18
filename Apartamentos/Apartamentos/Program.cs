using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection con User Secrets o variable de entorno.");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddDefaultIdentity<ApplicationUser>(o => {
    o.SignIn.RequireConfirmedAccount = false; // el registro publico esta bloqueado
    // Política sencilla: 8 caracteres mínimo, sin reglas de composición.
    o.Password.RequiredLength = 8;
    o.Password.RequireDigit = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequiredUniqueChars = 1;
    o.User.RequireUniqueEmail = true;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath="/Identity/Account/Login";
    o.AccessDeniedPath="/Identity/Account/AccessDenied";
    o.Cookie.HttpOnly=true;
    o.Cookie.SecurePolicy=CookieSecurePolicy.Always;
    o.SlidingExpiration=true;
    o.Events.OnValidatePrincipal=async ctx=> {
        var manager=ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user=await manager.GetUserAsync(ctx.Principal!);
        if(user is null || !user.Activo) {ctx.RejectPrincipal();await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);}
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddScoped<IArchivoPrivadoService, ArchivoPrivadoService>();
builder.Services.AddScoped<IConsultaRecibosService, ConsultaRecibosSeleniumService>();
builder.Services.AddScoped<IReporteService, ReporteService>();
var app = builder.Build();

// Acceso sin login exclusivamente en Development: localhost y, si se habilita,
// direcciones privadas de la LAN (RFC1918 / IPv6 local). Nunca desde IP publica.
var desactivarLoginTemporal = app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("Seguridad:DesactivarLoginTemporal");
var permitirLanSinLogin = app.Configuration.GetValue<bool>("Seguridad:PermitirLanSinLogin");
if (desactivarLoginTemporal)
    app.Logger.LogWarning(
        "Login temporal desactivado en Development: localhost{Lan}. No exponer el puerto a Internet.",
        permitirLanSinLogin ? " y dispositivos de red privada LAN" : " solamente");
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting();
app.UseAuthentication();
if (desactivarLoginTemporal)
{
    app.Use(async (context, next) =>
    {
        // Usar la IP de la conexion, no cabeceras X-Forwarded-For enviadas por el cliente.
        var ip = context.Connection.RemoteIpAddress;
        if (ip is not null)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        }
        bool esLocal = ip is not null && System.Net.IPAddress.IsLoopback(ip);
        bool esLan = permitirLanSinLogin && ip is not null && EsDireccionPrivada(ip);
        if (esLocal || esLan)
        {
            // Identidad de prueba por solicitud: no inicia sesion ni crea usuarios reales.
            context.Items["LoginTemporalDesactivado"] = true;
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "desarrollo-sin-login"),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Acceso temporal")
                    },
                    "DesarrolloSinLogin"));
            if (context.Request.Path.StartsWithSegments("/Identity/Account/Login"))
            {
                context.Response.Redirect("/");
                return;
            }
        }
        await next();
    });
}
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Solo direcciones privadas de red; no habilitar acceso anonimo desde IPs publicas.
static bool EsDireccionPrivada(System.Net.IPAddress direccion)
{
    if (direccion.IsIPv4MappedToIPv6) direccion = direccion.MapToIPv4();
    var partes = direccion.GetAddressBytes();
    if (partes.Length == 4)
        return partes[0] == 10 ||
               (partes[0] == 172 && partes[1] >= 16 && partes[1] <= 31) ||
               (partes[0] == 192 && partes[1] == 168);

    // fc00::/7 (ULA) y fe80::/10 (link-local).
    return partes.Length == 16 &&
           ((partes[0] & 0xFE) == 0xFC ||
            (partes[0] == 0xFE && (partes[1] & 0xC0) == 0x80));
}
// Requiere ejecutar antes: dotnet ef database update
await IdentitySeed.EnsureInitialUserAsync(app.Services, app.Configuration);
app.Run();
