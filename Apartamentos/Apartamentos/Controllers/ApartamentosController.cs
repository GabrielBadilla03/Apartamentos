using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;

[Authorize]
public sealed class ApartamentosController(ApplicationDbContext db):Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.Apartamentos
        .Include(a=>a.Agua).Include(a=>a.Luz).Include(a=>a.Encargados)
        .AsNoTracking().OrderBy(a=>a.Ubicacion).ThenBy(a=>a.DescripcionDepartamento).ToListAsync(ct));
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(CancellationToken ct) {await Selectores(ct);return View(new ApartamentoForm());}
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(ApartamentoForm form,CancellationToken ct)
    {
        await ValidarServicios(form,ct);
        if(!ModelState.IsValid){await Selectores(ct);return View(form);}
        db.Apartamentos.Add(new Apartamento{Ubicacion=form.Ubicacion,DescripcionDepartamento=form.DescripcionDepartamento,IdAgua=form.IdAgua,IdLuz=form.IdLuz});
        await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var a=await db.Apartamentos.FindAsync(new object[]{id},ct);if(a is null)return NotFound();
        await Selectores(ct);return View(new ApartamentoForm{IdApartamento=id,Ubicacion=a.Ubicacion,DescripcionDepartamento=a.DescripcionDepartamento,IdAgua=a.IdAgua,IdLuz=a.IdLuz});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,ApartamentoForm form,CancellationToken ct)
    {
        if(id!=form.IdApartamento)return BadRequest();
        await ValidarServicios(form,ct);
        if(!ModelState.IsValid){await Selectores(ct);return View(form);}
        var a=await db.Apartamentos.FindAsync(new object[]{id},ct);if(a is null)return NotFound();
        a.Ubicacion=form.Ubicacion;a.DescripcionDepartamento=form.DescripcionDepartamento;a.IdAgua=form.IdAgua;a.IdLuz=form.IdLuz;
        await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var a=await db.Apartamentos.Include(x=>x.Encargados).FirstOrDefaultAsync(x=>x.IdApartamento==id,ct);
        if(a is null)return NotFound();
        if(a.Encargados.Any())TempData["Error"]="No se puede eliminar el apartamento: tiene encargados asignados.";
        else{db.Apartamentos.Remove(a);await db.SaveChangesAsync(ct);TempData["Mensaje"]="Apartamento eliminado.";}
        return RedirectToAction(nameof(Index));
    }
    private async Task ValidarServicios(ApartamentoForm f,CancellationToken ct)
    {
        if(f.IdAgua.HasValue && !await db.Aguas.AnyAsync(x=>x.IdAgua==f.IdAgua,ct))ModelState.AddModelError(nameof(f.IdAgua),"El servicio de agua no existe.");
        if(f.IdLuz.HasValue && !await db.Luces.AnyAsync(x=>x.IdLuz==f.IdLuz,ct))ModelState.AddModelError(nameof(f.IdLuz),"El servicio de luz no existe.");
    }
    private async Task Selectores(CancellationToken ct)
    {
        ViewBag.Aguas=await db.Aguas.AsNoTracking().OrderBy(x=>x.IdAgua).ToListAsync(ct);
        ViewBag.Luces=await db.Luces.AsNoTracking().OrderBy(x=>x.IdLuz).ToListAsync(ct);
    }
}
