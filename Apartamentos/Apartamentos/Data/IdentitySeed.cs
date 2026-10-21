using Apartamentos.Models;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Data;

public static class IdentitySeed
{
    public static async Task CreateRolesAndAdminAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Administrador", "Usuario" })
            if (!await roleManager.RoleExistsAsync(role))
            {
                var r = await roleManager.CreateAsync(new IdentityRole(role));
                if (!r.Succeeded) throw new InvalidOperationException(string.Join("; ",r.Errors.Select(e => e.Description)));
            }
        var email = config["ADMIN_BOOTSTRAP_EMAIL"];
        var password = config["ADMIN_BOOTSTRAP_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // Nunca reasignar derechos automáticamente a un usuario preexistente.
        if (await manager.FindByEmailAsync(email) != null) return;
        if (manager.Users.Any()) return;
        var u = new ApplicationUser { UserName=email, Email=email, EmailConfirmed=true, Nombre="Administrador" };
        var result = await manager.CreateAsync(u, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ",result.Errors.Select(e=>e.Description)));
        await manager.AddToRoleAsync(u, "Administrador");
    }
}
