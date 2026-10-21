using Apartamentos.Data;
using Apartamentos.Models;
using Apartamentos.Services;
using Apartamentos.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Controllers;

[Authorize]
public sealed class AguaController(ApplicationDbContext db, IConsultaRecibosService consulta, IReporteService reporte, ILogger<AguaController> log) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items=await db.Aguas.Include(x=>x.Apartamentos).AsNoTracking().OrderBy(x=>x.IdAgua).ToListAsync(ct);
        return View(items);
    }
    [Authorize(Roles="Administrador")]
    public IActionResult Create() => View(new ServicioForm());
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Create(ServicioForm form,CancellationToken ct)
    {
        if(!ModelState.IsValid)return View(form);
        if(await db.Aguas.AnyAsync(x=>x.IdAgua==form.Id,ct)){ModelState.AddModelError(nameof(form.Id),"El servicio ya existe.");return View(form);}
        db.Aguas.Add(new Agua{IdAgua=form.Id,Titular=form.Titular,FechaVencimiento=form.FechaVencimiento,
            FechaPagar=form.FechaPagar,FechaInicioPago=form.FechaInicioPago,Pagar=form.Pagar});
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio registrado.";return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.Aguas.FindAsync(new object[]{id},ct);
        return x is null?NotFound():View(new ServicioForm{Id=x.IdAgua,Titular=x.Titular,
            FechaVencimiento=x.FechaVencimiento,FechaPagar=x.FechaPagar,FechaInicioPago=x.FechaInicioPago,Pagar=x.Pagar});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Edit(long id,ServicioForm form,CancellationToken ct)
    {
        if(id!=form.Id)return BadRequest();
        if(!ModelState.IsValid)return View(form);
        var x=await db.Aguas.FindAsync(new object[]{id},ct);
        if(x is null)return NotFound();
        x.Titular=form.Titular;x.FechaVencimiento=form.FechaVencimiento;x.FechaPagar=form.FechaPagar;
        x.FechaInicioPago=form.FechaInicioPago;x.Pagar=form.Pagar;
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Cambios guardados.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.Aguas.Include(x=>x.Apartamentos).Include(x=>x.MontosAgua).FirstOrDefaultAsync(x=>x.IdAgua==id,ct);
        if(x is null)return NotFound();
        if(x.Apartamentos.Any()||x.MontosAgua.Any()){TempData["Error"]="No se puede eliminar: hay apartamentos o montos vinculados.";return RedirectToAction(nameof(Index));}
        db.Aguas.Remove(x);await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio eliminado.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ConsultarSinRegistrar(long id,CancellationToken ct)
    {
        if(!await db.Aguas.AnyAsync(x=>x.IdAgua==id,ct))return NotFound();
        try
        {
            var r=await consulta.ConsultarAsync("agua",id,ct);
            TempData["Mensaje"]=$"NIS {id}: importe consultado {r.Monto:N2}; vencimiento {r.FechaVencimiento?.ToString("dd/MM/yyyy")??"no disponible"}. No se guardaron cambios.";
        }
        catch(Exception ex){log.LogError(ex,"Error consulta sin registro {Id}",id);TempData["Error"]="Consulta fallida: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ConsultarRegistrar(long id,CancellationToken ct)
    {
        var x=await db.Aguas.FindAsync(new object[]{id},ct);if(x is null)return NotFound();
        try
        {
            var r=await consulta.ConsultarAsync("agua",id,ct);
            if(r.FechaVencimiento.HasValue)x.FechaVencimiento=r.FechaVencimiento.Value.Date;
            if(x.Pagar && r.Monto>0)
            {
                // Una consulta repetida del mismo día no debe duplicar el mismo importe.
                var hoy=DateTime.Today;
                var ya=await db.MontosAgua.AnyAsync(m=>m.IdAgua==id && m.FechaRegistro==hoy && m.Monto==r.Monto,ct);
                if(!ya)db.MontosAgua.Add(new MontoAgua{IdAgua=id,Monto=r.Monto,FechaRegistro=hoy});
            }
            await db.SaveChangesAsync(ct);
            TempData["Mensaje"]=$"Consulta terminada. Monto: {r.Monto:N2}. No es un pago bancario.";
        }
        catch(Exception ex){log.LogError(ex,"Error de consulta agua: {Id}",id);TempData["Error"]="No se pudo consultar el proveedor: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> ActualizarFechas(CancellationToken ct)
    {
        var servicios=await db.Aguas.Where(x=>x.Pagar).ToListAsync(ct);
        int ok=0,fallos=0;
        foreach(var x in servicios)
        {
            try{var r=await consulta.ConsultarAsync("agua",x.IdAgua,ct);
                if(r.FechaVencimiento.HasValue){x.FechaVencimiento=r.FechaVencimiento.Value.Date;await db.SaveChangesAsync(ct);ok++;}}
            catch(Exception ex){fallos++;log.LogWarning(ex,"Error consultando servicio {Id}",x.IdAgua);}
        }
        TempData["Mensaje"]=$"Fechas actualizadas: {ok}; fallidas: {fallos}.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Administrador")]
    public async Task<IActionResult> AvanzarPeriodo(CancellationToken ct)
    {
        var items=await db.Aguas.ToListAsync(ct);
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
        var pdf=await reporte.GenerarServiciosAsync(true,montos,ct);
        return File(pdf,"application/pdf",montos?"Montos_agua.pdf":"Servicios_agua.pdf");
    }
}
