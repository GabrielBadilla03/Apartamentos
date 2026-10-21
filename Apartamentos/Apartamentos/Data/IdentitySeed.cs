using Apartamentos.Models;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Data;

public static class IdentitySeed
{
    // No crea roles ni asigna permisos. Solo permite iniciar el sistema
    // con el usuario inicial cuando la base todavia no tiene cuentas.
    public static async Task EnsureInitialUserAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (users.Users.Any()) return;

        var email = config["ADMIN_BOOTSTRAP_EMAIL"];
        var password = config["ADMIN_BOOTSTRAP_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var initial = new ApplicationUser
        {
            UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true,
            Activo = true, Nombre = "Usuario inicial"
        };
        var result = await users.CreateAsync(initial, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
