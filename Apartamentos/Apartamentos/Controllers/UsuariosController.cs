using Apartamentos.Models;
using Apartamentos.Services;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Apartamentos.Controllers;

// Solo exige una sesion iniciada; no hay comprobaciones de roles.
[Authorize]
public sealed class UsuariosController(
    UserManager<ApplicationUser> users,
    IArchivoPrivadoService archivos,
    ILogger<UsuariosController> logger) : Controller
{
    public IActionResult Index() => View(users.Users.OrderBy(x => x.Nombre).ToList());

    public IActionResult Create() => View(new CrearUsuarioForm());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Create(CrearUsuarioForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        var nuevo = new ApplicationUser
        {
            UserName = form.Email.Trim(), Email = form.Email.Trim(), EmailConfirmed = true,
            Nombre = form.Nombre.Trim(), Apellidos = form.Apellidos.Trim(),
            PhoneNumber = form.Telefono, Activo = true
        };
        var result = await users.CreateAsync(nuevo, form.Password);
        if (!result.Succeeded) { AddErrors(result); return View(form); }

        if (form.Imagen is { Length: > 0 })
        {
            try
            {
                nuevo.RutaImagen = await archivos.GuardarAsync(form.Imagen, ct);
                var saved = await users.UpdateAsync(nuevo);
                if (!saved.Succeeded) { AddErrors(saved); TempData["Error"] = "Cuenta creada, pero no se guardo la imagen."; }
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = "Cuenta creada, pero la imagen no se pudo guardar: " + ex.Message;
            }
        }
        logger.LogInformation("Usuario {Editor} creo cuenta {Id}", User.Identity?.Name, nuevo.Id);
        TempData["Mensaje"] = "Cuenta creada correctamente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        return View(new EditarUsuarioForm
        {
            Id = u.Id, Email = u.Email ?? "", Nombre = u.Nombre, Apellidos = u.Apellidos,
            Telefono = u.PhoneNumber, Activo = u.Activo
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Edit(string id, EditarUsuarioForm form, CancellationToken ct)
    {
        if (id != form.Id) return BadRequest();
        if (!ModelState.IsValid) return View(form);
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();

        // Evita que un usuario se desactive a si mismo por accidente.
        if (id == User.FindFirstValue(ClaimTypes.NameIdentifier) && !form.Activo)
        {
            ModelState.AddModelError(string.Empty, "No puedes desactivar tu propia cuenta.");
            return View(form);
        }

        u.Nombre = form.Nombre.Trim();
        u.Apellidos = form.Apellidos.Trim();
        u.PhoneNumber = form.Telefono;
        u.Activo = form.Activo;
        if (!string.Equals(u.Email, form.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var changed = await users.SetEmailAsync(u, form.Email.Trim());
            if (!changed.Succeeded) { AddErrors(changed); return View(form); }
            changed = await users.SetUserNameAsync(u, form.Email.Trim());
            if (!changed.Succeeded) { AddErrors(changed); return View(form); }
            u.EmailConfirmed = true;
        }
        if (form.Imagen is { Length: > 0 })
        {
            try { u.RutaImagen = await archivos.GuardarAsync(form.Imagen, ct); }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(form.Imagen), ex.Message);
                return View(form);
            }
        }
        var result = await users.UpdateAsync(u);
        if (!result.Succeeded) { AddErrors(result); return View(form); }
        if (form.Activo && await users.IsLockedOutAsync(u))
            await users.SetLockoutEndDateAsync(u, null);
        await users.UpdateSecurityStampAsync(u);
        logger.LogInformation("Usuario {Editor} actualizo cuenta {Id}", User.Identity?.Name, id);
        TempData["Mensaje"] = "Usuario actualizado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == id)
        {
            TempData["Error"] = "No puedes eliminar tu propia cuenta.";
            return RedirectToAction(nameof(Index));
        }
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) { AddErrors(result); TempData["Error"] = "No se pudo eliminar la cuenta."; }
        else TempData["Mensaje"] = "Usuario eliminado.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await users.FindByIdAsync(id);
        return user is null ? NotFound() : View(model: user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string nuevaPassword)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "La contraseña debe tener al menos 8 caracteres.");
            return View(model: user);
        }
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, nuevaPassword);
        if (!result.Succeeded) { AddErrors(result); return View(model: user); }
        logger.LogWarning("Usuario {Editor} cambio la clave de {Id}", User.Identity?.Name, id);
        TempData["Mensaje"] = "Contraseña actualizada.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Imagen(string id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var photo = await archivos.LeerAsync(user.RutaImagen, ct);
        if (photo is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return File(photo.Value.Contenido, photo.Value.Mime);
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
