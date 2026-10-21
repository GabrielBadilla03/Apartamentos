using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection con User Secrets o variable de entorno.");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddDefaultIdentity<ApplicationUser>(o => {
    o.SignIn.RequireConfirmedAccount = false; // solo usuarios creados por admin
    o.Password.RequiredLength = 10;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
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
QuestPDF.Settings.License = LicenseType.Community; // verificar elegibilidad de licencia antes de desplegar
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting();
app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
// Requiere ejecutar antes: dotnet ef database update
await IdentitySeed.CreateRolesAndAdminAsync(app.Services, app.Configuration);
app.Run();
