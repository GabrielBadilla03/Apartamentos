using Apartamentos.Models;
using Apartamentos.Services;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Apartamentos.Controllers;

[Authorize(Roles="Administrador")]
public sealed class UsuariosController(UserManager<ApplicationUser> users,RoleManager<IdentityRole> roles,IArchivoPrivadoService archivos,ILogger<UsuariosController> logger) : Controller
{
    public IActionResult Index()=>View(users.Users.OrderBy(x=>x.Nombre).ToList());
    public IActionResult Create()=>View(new CrearUsuarioForm());
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Create(CrearUsuarioForm form,CancellationToken ct)
    {
        if(!await roles.RoleExistsAsync(form.Rol)) ModelState.AddModelError(nameof(form.Rol),"Rol inexistente.");
        if(!ModelState.IsValid)return View(form);
        var user=new ApplicationUser{UserName=form.Email,Email=form.Email,EmailConfirmed=true,Nombre=form.Nombre,Apellidos=form.Apellidos,PhoneNumber=form.Telefono,Activo=true};
        var result=await users.CreateAsync(user,form.Password);
        if(!result.Succeeded){Errores(result);return View(form);}
        var roleResult=await users.AddToRoleAsync(user,form.Rol);
        if(!roleResult.Succeeded)
        {
            // Revert incomplete account creation if no role could be assigned.
            await users.DeleteAsync(user);
            Errores(roleResult);
            return View(form);
        }
        if(form.Imagen is {Length:>0})
        {
            try{user.RutaImagen=await archivos.GuardarAsync(form.Imagen,ct);await users.UpdateAsync(user);}
            catch(InvalidOperationException ex){TempData["Error"]="Usuario creado, pero la imagen fue rechazada: "+ex.Message;}
        }
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Edit(string id)
    {
        var x=await users.FindByIdAsync(id);if(x is null)return NotFound();
        return View(new EditarUsuarioForm{Id=x.Id,Email=x.Email??"",Nombre=x.Nombre,Apellidos=x.Apellidos,Telefono=x.PhoneNumber,Activo=x.Activo,Rol=(await users.GetRolesAsync(x)).FirstOrDefault()??"Usuario"});
    }
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Edit(string id,EditarUsuarioForm form,CancellationToken ct)
    {
        if(id!=form.Id)return BadRequest();
        if(!await roles.RoleExistsAsync(form.Rol))ModelState.AddModelError(nameof(form.Rol),"Rol inválido.");
        if(!ModelState.IsValid)return View(form);
        var x=await users.FindByIdAsync(id);if(x is null)return NotFound();
        if(x.Id==User.FindFirstValue(ClaimTypes.NameIdentifier) && (!form.Activo || form.Rol!="Administrador"))
        {ModelState.AddModelError("","No puede desactivarse ni quitarse su propio rol administrador.");return View(form);}
        x.Nombre=form.Nombre;x.Apellidos=form.Apellidos;x.PhoneNumber=form.Telefono;x.Activo=form.Activo;
        if(x.Email!=form.Email)
        {
            var r=await users.SetEmailAsync(x,form.Email);if(!r.Succeeded){Errores(r);return View(form);}
            r=await users.SetUserNameAsync(x,form.Email);if(!r.Succeeded){Errores(r);return View(form);}
            x.EmailConfirmed=true;
        }
        if(form.Imagen is {Length:>0})
        {
            try{x.RutaImagen=await archivos.GuardarAsync(form.Imagen,ct);}
            catch(InvalidOperationException ex){ModelState.AddModelError(nameof(form.Imagen),ex.Message);return View(form);}
        }
        var result=await users.UpdateAsync(x);if(!result.Succeeded){Errores(result);return View(form);}
        if(form.Activo && await users.IsLockedOutAsync(x))await users.SetLockoutEndDateAsync(x,null);
        var oldRoles=await users.GetRolesAsync(x);
        var remove=await users.RemoveFromRolesAsync(x,oldRoles);
        if(!remove.Succeeded){Errores(remove);return View(form);}
        var add=await users.AddToRoleAsync(x,form.Rol);
        if(!add.Succeeded){Errores(add);return View(form);}
        await users.UpdateSecurityStampAsync(x);
        logger.LogInformation("Admin {Admin} actualizó usuario {Id}",User.Identity?.Name,id);
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if(User.FindFirstValue(ClaimTypes.NameIdentifier)==id){TempData["Error"]="No puede eliminarse su propia cuenta.";return RedirectToAction(nameof(Index));}
        var x=await users.FindByIdAsync(id);if(x is null)return NotFound();
        var result=await users.DeleteAsync(x);
        if(!result.Succeeded){Errores(result);TempData["Error"]="No se pudo eliminar el usuario.";}
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> ResetPassword(string id)
    {
        var x=await users.FindByIdAsync(id);return x is null?NotFound():View(model:x);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id,string nuevaPassword)
    {
        var x=await users.FindByIdAsync(id);if(x is null)return NotFound();
        if(string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length<10)
        {ModelState.AddModelError("","La contraseña debe tener al menos 10 caracteres.");return View(model:x);}
        var token=await users.GeneratePasswordResetTokenAsync(x);
        var result=await users.ResetPasswordAsync(x,token,nuevaPassword);
        if(!result.Succeeded){Errores(result);return View(model:x);}
        logger.LogWarning("Administrador {Admin} restableció contraseña de {Id}",User.Identity?.Name,id);
        TempData["Mensaje"]="Contraseña restablecida. Entréguela mediante un canal seguro.";
        return RedirectToAction(nameof(Index));
    }
    [HttpGet]
    public async Task<IActionResult> Imagen(string id,CancellationToken ct)
    {
        var x=await users.FindByIdAsync(id);if(x is null)return NotFound();
        var f=await archivos.LeerAsync(x.RutaImagen,ct);if(f is null)return NotFound();
        Response.Headers.CacheControl="private, no-store";
        return File(f.Value.Contenido,f.Value.Mime);
    }
    private void Errores(IdentityResult result){foreach(var e in result.Errors)ModelState.AddModelError("",e.Description);}
}
