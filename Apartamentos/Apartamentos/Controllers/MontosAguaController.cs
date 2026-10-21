using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;
[Authorize]
public sealed class MontosAguaController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(long? idServicio,CancellationToken ct)
    {
        var q=db.MontosAgua.AsNoTracking().AsQueryable();
        if(idServicio.HasValue)q=q.Where(x=>x.IdAgua==idServicio);
        ViewBag.IdServicio=idServicio;
        var datos=await q.OrderByDescending(x=>x.FechaRegistro).ThenByDescending(x=>x.IdMonto).Take(1000).ToListAsync(ct);
        return View(datos);
    }
    [Authorize(Roles="Administrador")]
    public IActionResult Create(long? idServicio)=>View(new MontoForm{IdServicio=idServicio,FechaRegistro=DateTime.Today});
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(MontoForm form,CancellationToken ct)
    {
        if(!form.IdServicio.HasValue || !await db.Aguas.AnyAsync(x=>x.IdAgua==form.IdServicio,ct))
            ModelState.AddModelError(nameof(form.IdServicio),"Servicio inexistente.");
        if(!ModelState.IsValid)return View(form);
        db.MontosAgua.Add(new MontoAgua{IdAgua=form.IdServicio,Monto=form.Monto,FechaRegistro=form.FechaRegistro.Date});
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index),new{idServicio=form.IdServicio});
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.MontosAgua.FindAsync(new object[]{id},ct);
        return x is null?NotFound():View(new MontoForm{IdMonto=x.IdMonto,IdServicio=x.IdAgua,Monto=x.Monto,FechaRegistro=x.FechaRegistro});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,MontoForm form,CancellationToken ct)
    {
        if(id!=form.IdMonto)return BadRequest();
        if(!form.IdServicio.HasValue || !await db.Aguas.AnyAsync(x=>x.IdAgua==form.IdServicio,ct))ModelState.AddModelError(nameof(form.IdServicio),"Servicio inexistente.");
        if(!ModelState.IsValid)return View(form);
        var x=await db.MontosAgua.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        x.IdAgua=form.IdServicio;x.Monto=form.Monto;x.FechaRegistro=form.FechaRegistro.Date;
        await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Index),new{idServicio=form.IdServicio});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.MontosAgua.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        var idServicio=x.IdAgua;db.MontosAgua.Remove(x);await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index),new{idServicio});
    }
}
