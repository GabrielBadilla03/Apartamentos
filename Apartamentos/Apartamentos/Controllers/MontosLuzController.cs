using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;
[Authorize]
public sealed class MontosLuzController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(long? idServicio,CancellationToken ct)
    {
        var q=db.MontosLuz.AsNoTracking().AsQueryable();
        if(idServicio.HasValue)q=q.Where(x=>x.IdLuz==idServicio);
        ViewBag.IdServicio=idServicio;
        var datos=await q.OrderByDescending(x=>x.FechaRegistro).ThenByDescending(x=>x.IdMonto).Take(1000).ToListAsync(ct);
        return View(datos);
    }
    [Authorize(Roles="Administrador")]
    public IActionResult Create(long? idServicio)=>View(new MontoForm{IdServicio=idServicio,FechaRegistro=DateTime.Today});
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(MontoForm form,CancellationToken ct)
    {
        if(!form.IdServicio.HasValue || !await db.Luces.AnyAsync(x=>x.IdLuz==form.IdServicio,ct))
            ModelState.AddModelError(nameof(form.IdServicio),"Servicio inexistente.");
        if(!ModelState.IsValid)return View(form);
        db.MontosLuz.Add(new MontoLuz{IdLuz=form.IdServicio,Monto=form.Monto,FechaRegistro=form.FechaRegistro.Date});
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index),new{idServicio=form.IdServicio});
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.MontosLuz.FindAsync(new object[]{id},ct);
        return x is null?NotFound():View(new MontoForm{IdMonto=x.IdMonto,IdServicio=x.IdLuz,Monto=x.Monto,FechaRegistro=x.FechaRegistro});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,MontoForm form,CancellationToken ct)
    {
        if(id!=form.IdMonto)return BadRequest();
        if(!form.IdServicio.HasValue || !await db.Luces.AnyAsync(x=>x.IdLuz==form.IdServicio,ct))ModelState.AddModelError(nameof(form.IdServicio),"Servicio inexistente.");
        if(!ModelState.IsValid)return View(form);
        var x=await db.MontosLuz.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        x.IdLuz=form.IdServicio;x.Monto=form.Monto;x.FechaRegistro=form.FechaRegistro.Date;
        await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index),new{idServicio=form.IdServicio});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.MontosLuz.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        var idServicio=x.IdLuz;db.MontosLuz.Remove(x);await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index),new{idServicio});
    }
}
