using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.Services;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;

[Authorize]
public sealed class LuzController(ApplicationDbContext db, IConsultaRecibosService consulta, IReporteService reporte, ILogger<LuzController> log) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items=await db.Luces.Include(x=>x.Apartamentos).AsNoTracking().OrderBy(x=>x.IdLuz).ToListAsync(ct);
        return View(items);
    }
    [Authorize(Roles="Administrador")]
    public IActionResult Create() => View(new ServicioForm());
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(ServicioForm form,CancellationToken ct)
    {
        if(!ModelState.IsValid)return View(form);
        if(await db.Luces.AnyAsync(x=>x.IdLuz==form.Id,ct)){ModelState.AddModelError(nameof(form.Id),"El servicio ya existe.");return View(form);}
        db.Luces.Add(new Luz{IdLuz=form.Id,Titular=form.Titular,FechaVencimiento=form.FechaVencimiento,
            FechaPagar=form.FechaPagar,FechaInicioPago=form.FechaInicioPago,Pagar=form.Pagar});
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio registrado.";return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.Luces.FindAsync(new object[]{id},ct);
        return x is null?NotFound():View(new ServicioForm{Id=x.IdLuz,Titular=x.Titular,
            FechaVencimiento=x.FechaVencimiento,FechaPagar=x.FechaPagar,FechaInicioPago=x.FechaInicioPago,Pagar=x.Pagar});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,ServicioForm form,CancellationToken ct)
    {
        if(id!=form.Id)return BadRequest();
        if(!ModelState.IsValid)return View(form);
        var x=await db.Luces.FindAsync(new object[]{id},ct);
        if(x is null)return NotFound();
        x.Titular=form.Titular;x.FechaVencimiento=form.FechaVencimiento;x.FechaPagar=form.FechaPagar;
        x.FechaInicioPago=form.FechaInicioPago;x.Pagar=form.Pagar;
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Cambios guardados.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.Luces.Include(x=>x.Apartamentos).Include(x=>x.MontosLuz).FirstOrDefaultAsync(x=>x.IdLuz==id,ct);
        if(x is null)return NotFound();
        if(x.Apartamentos.Any()||x.MontosLuz.Any()){TempData["Error"]="No se puede eliminar: hay apartamentos o montos vinculados.";return RedirectToAction(nameof(Index));}
        db.Luces.Remove(x);await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio eliminado.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ConsultarSinRegistrar(long id,CancellationToken ct)
    {
        if(!await db.Luces.AnyAsync(x=>x.IdLuz==id,ct))return NotFound();
        try
        {
            var r=await consulta.ConsultarAsync("luz",id,ct);
            TempData["Mensaje"]=$"NIS {id}: importe consultado {r.Monto:N2}; vencimiento {r.FechaVencimiento?.ToString("dd/MM/yyyy")??"no disponible"}. No se guardaron cambios.";
        }
        catch(Exception ex){log.LogError(ex,"Error consulta sin registro {Id}",id);TempData["Error"]="Consulta fallida: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ConsultarRegistrar(long id,CancellationToken ct)
    {
        var x=await db.Luces.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        try
        {
            var r=await consulta.ConsultarAsync("luz",id,ct);
            if(r.FechaVencimiento.HasValue)x.FechaVencimiento=r.FechaVencimiento.Value.Date;
            if(x.Pagar && r.Monto>0)
            {
                // Una consulta repetida del mismo día no debe duplicar el mismo importe.
                var hoy=DateTime.Today;
                var ya=await db.MontosLuz.AnyAsync(m=>m.IdLuz==id && m.FechaRegistro==hoy && m.Monto==r.Monto,ct);
                if(!ya)db.MontosLuz.Add(new MontoLuz{IdLuz=id,Monto=r.Monto,FechaRegistro=hoy});
            }
            await db.SaveChangesAsync(ct);
            TempData["Mensaje"]=$"Consulta terminada. Monto: {r.Monto:N2}. No es un pago bancario.";
        }
        catch(Exception ex){log.LogError(ex,"Error de consulta luz: {Id}",id);TempData["Error"]="No se pudo consultar el proveedor: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ActualizarFechas(CancellationToken ct)
    {
        var servicios=await db.Luces.Where(x=>x.Pagar).ToListAsync(ct);
        int ok=0,fallos=0;
        foreach(var x in servicios)
        {
            try{var r=await consulta.ConsultarAsync("luz",x.IdLuz,ct);
                if(r.FechaVencimiento.HasValue){x.FechaVencimiento=r.FechaVencimiento.Value.Date;await db.SaveChangesAsync(ct);ok++;}}
            catch(Exception ex){fallos++;log.LogWarning(ex,"Error consultando servicio {Id}",x.IdLuz);}
        }
        TempData["Mensaje"]=$"Fechas actualizadas: {ok}; fallidas: {fallos}.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> AvanzarPeriodo(CancellationToken ct)
    {
        var items=await db.Luces.ToListAsync(ct);
        foreach(var x in items)
        {
            if(x.FechaPagar.HasValue)while(x.FechaPagar.Value.Date<DateTime.Today)x.FechaPagar=x.FechaPagar.Value.AddMonths(1);
            if(x.FechaInicioPago.HasValue)while(x.FechaInicioPago.Value.Date<DateTime.Today)x.FechaInicioPago=x.FechaInicioPago.Value.AddMonths(1);
        }
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Fechas de ciclo de pago actualizadas.";
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Descargar(bool montos=false,CancellationToken ct=default)
    {
        var pdf=await reporte.GenerarServiciosAsync(false,montos,ct);
        return File(pdf,"application/pdf",montos?"Montos_luz.pdf":"Servicios_luz.pdf");
    }
}
