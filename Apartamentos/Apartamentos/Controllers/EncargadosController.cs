using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.Services;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;

[Authorize]
public sealed class EncargadosController(ApplicationDbContext db,IArchivoPrivadoService archivos):Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.Encargados.Include(x=>x.Apartamento)
        .AsNoTracking().OrderBy(x=>x.Nombre).ThenBy(x=>x.Apellidos).ToListAsync(ct));
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(CancellationToken ct) {await Selectores(ct);return View(new EncargadoForm());}
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador"),RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Create(EncargadoForm form,CancellationToken ct)
    {
        await ValidarApartamento(form,ct);
        if(!ModelState.IsValid){await Selectores(ct);return View(form);}
        var x=new Encargado{Nombre=form.Nombre,Apellidos=form.Apellidos,Cedula=form.Cedula,IdApartamento=form.IdApartamento};
        try {if(form.Imagen is {Length:>0}) x.FotoCedula=await archivos.GuardarAsync(form.Imagen,ct);}
        catch(InvalidOperationException ex){ModelState.AddModelError(nameof(form.Imagen),ex.Message);await Selectores(ct);return View(form);}
        db.Encargados.Add(x);await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.Encargados.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        await Selectores(ct);return View(new EncargadoForm{IdEncargado=x.IdEncargado,Nombre=x.Nombre,Apellidos=x.Apellidos,Cedula=x.Cedula,IdApartamento=x.IdApartamento});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador"),RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Edit(long id,EncargadoForm form,CancellationToken ct)
    {
        if(id!=form.IdEncargado)return BadRequest();
        await ValidarApartamento(form,ct);
        if(!ModelState.IsValid){await Selectores(ct);return View(form);}
        var x=await db.Encargados.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        x.Nombre=form.Nombre;x.Apellidos=form.Apellidos;x.Cedula=form.Cedula;x.IdApartamento=form.IdApartamento;
        try{if(form.Imagen is {Length:>0})x.FotoCedula=await archivos.GuardarAsync(form.Imagen,ct);}
        catch(InvalidOperationException ex){ModelState.AddModelError(nameof(form.Imagen),ex.Message);await Selectores(ct);return View(form);}
        await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.Encargados.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        db.Encargados.Remove(x);await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Imagen(long id,CancellationToken ct)
    {
        var refArchivo=await db.Encargados.Where(x=>x.IdEncargado==id).Select(x=>x.FotoCedula).FirstOrDefaultAsync(ct);
        var f=await archivos.LeerAsync(refArchivo,ct);
        if(f is null) return NotFound("No es una imagen local; puede tratarse de una URL heredada de Firebase.");
        Response.Headers.CacheControl="private, no-store";
        return File(f.Value.Contenido,f.Value.Mime);
    }
    private async Task ValidarApartamento(EncargadoForm f,CancellationToken ct)
    {
        if(f.IdApartamento.HasValue && !await db.Apartamentos.AnyAsync(a=>a.IdApartamento==f.IdApartamento,ct))ModelState.AddModelError(nameof(f.IdApartamento),"Apartamento no encontrado.");
    }
    private async Task Selectores(CancellationToken ct)=>ViewBag.Apartamentos=await db.Apartamentos.AsNoTracking().OrderBy(x=>x.Ubicacion).ToListAsync(ct);
}
