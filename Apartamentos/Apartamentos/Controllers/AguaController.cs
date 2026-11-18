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
        public IActionResult Create() => View(new ServicioForm());
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServicioForm form,CancellationToken ct)
    {
        if(!ModelState.IsValid)return View(form);
        if(await db.Aguas.AnyAsync(x=>x.IdAgua==form.Id,ct)){ModelState.AddModelError(nameof(form.Id),"El servicio ya existe.");return View(form);}
        db.Aguas.Add(new Agua{IdAgua=form.Id,Titular=form.Titular,FechaVencimiento=form.FechaVencimiento,
            FechaPagar=form.FechaPagar,FechaInicioPago=form.FechaInicioPago,Pagar=form.Pagar});
        await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio registrado.";return RedirectToAction(nameof(Index));
    }
        public async Task<IActionResult> Edit(long id,CancellationToken ct)
    {
        var x=await db.Aguas.FindAsync(new object[]{id},ct);
        return x is null?NotFound():View(new ServicioForm{Id=x.IdAgua,Titular=x.Titular,
            FechaVencimiento=x.FechaVencimiento,FechaPagar=x.FechaPagar,FechaInicioPago=x.FechaInicioPago,Pagar=x.Pagar});
    }
    [HttpPost,ValidateAntiForgeryToken]
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
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        var x=await db.Aguas.Include(x=>x.Apartamentos).Include(x=>x.MontosAgua).AsSplitQuery().FirstOrDefaultAsync(x=>x.IdAgua==id,ct);
        if(x is null)return NotFound();
        if(x.Apartamentos.Any()||x.MontosAgua.Any()){TempData["Error"]="No se puede eliminar: hay apartamentos o montos vinculados.";return RedirectToAction(nameof(Index));}
        db.Aguas.Remove(x);await db.SaveChangesAsync(ct);TempData["Mensaje"]="Servicio eliminado.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsultarSinRegistrar(long id,CancellationToken ct)
    {
        if(!await db.Aguas.AnyAsync(x=>x.IdAgua==id,ct))return NotFound();
        try
        {
            var r=await consulta.ConsultarAsync("agua",id,ct);
            TempData["Mensaje"]=$"NIS {id}: importe consultado {r.Monto:N2}; vencimiento {r.FechaVencimiento?.ToString("dd/MM/yyyy")??"no disponible"}. No se guardaron cambios.";
        }
        catch(Exception ex){log.LogError(ex,"Error consulta sin registro {Id}",id);TempData["Error"]="No se pudo consultar la pagina oficial: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
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
        catch(Exception ex){log.LogError(ex,"Error de consulta agua: {Id}",id);TempData["Error"]="Consulta automatica no completada: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarFechas(CancellationToken ct)
    {
        // Guardar inmediatamente cada NIS. Anteriormente ConsultarLoteAsync
        // terminaba todo el navegador antes de comenzar la primera actualizacion.
        var servicios = await db.Aguas.AsNoTracking()
            .Where(x => x.Pagar)
            .OrderBy(x => x.IdAgua)
            .Select(x => new { x.IdAgua, x.FechaVencimiento })
            .ToListAsync(ct);

        if (servicios.Count == 0)
        {
            TempData["Mensaje"] = "No hay servicios marcados para pagar.";
            return RedirectToAction(nameof(Index));
        }

        var fechasActuales = servicios.ToDictionary(x => x.IdAgua, x => x.FechaVencimiento);
        int actualizados = 0, sinCambio = 0, sinFecha = 0, fallidos = 0;
        int erroresGuardado = 0;
        string? errorGeneral = null;

        try
        {
            // AyA mantiene abierto un solo Brave y va informando cada consulta.
            // Al recibir una fecha valida se guarda sin esperar al siguiente NIS.
            await foreach (var item in consulta.ConsultarLoteProgresivoAsync(
                "agua", servicios.Select(x => x.IdAgua).ToArray(), ct))
            {
                ct.ThrowIfCancellationRequested();
                if (item.Error is not null || item.Resultado is null)
                {
                    fallidos++;
                    log.LogWarning("NIS {Nis}: no se obtuvo vencimiento. {Error}",
                        item.NumeroServicio, item.Error ?? "Sin respuesta");
                    continue;
                }

                if (item.Resultado.FechaVencimiento is not DateTime fechaConsultada)
                {
                    sinFecha++;
                    continue;
                }

                var fecha = fechaConsultada.Date;
                if (fechasActuales[item.NumeroServicio]?.Date == fecha)
                {
                    sinCambio++;
                    continue;
                }

                try
                {
                    // Evitar tracking de entidades de consultas anteriores.
                    int afectadas = await db.Aguas
                        .Where(x => x.IdAgua == item.NumeroServicio && x.Pagar)
                        .ExecuteUpdateAsync(x => x.SetProperty(
                            s => s.FechaVencimiento, fecha), ct);
                    if (afectadas == 1)
                    {
                        fechasActuales[item.NumeroServicio] = fecha;
                        actualizados++;
                        log.LogInformation("AyA fecha GUARDADA para NIS {Nis}: {Fecha:dd/MM/yyyy}",
                            item.NumeroServicio, fecha);
                    }
                    else
                    {
                        erroresGuardado++;
                        log.LogWarning("NIS {Nis}: no se actualizo ninguna fila en agua.",
                            item.NumeroServicio);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    erroresGuardado++;
                    log.LogError(ex, "Error guardando fecha de AyA para NIS {Nis}. Se continua con el siguiente.",
                        item.NumeroServicio);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.LogWarning("Se cancelo Actualizar Fechas AyA, ya guardadas: {Actualizados}.", actualizados);
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo general en Actualizar Fechas AyA despues de guardar {Actualizados} NIS.",
                actualizados);
            errorGeneral = ex.Message;
        }

        TempData["Mensaje"] =
            $"AyA: fechas guardadas {actualizados}; sin cambios {sinCambio}; sin fecha {sinFecha}; " +
            $"consultas fallidas {fallidos}; errores al guardar {erroresGuardado}.";

        if (errorGeneral is not null)
            TempData["Error"] = "Se interrumpio la consulta de AyA, pero se conservaron las fechas ya guardadas. " +
                "Detalle: " + errorGeneral;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost,ValidateAntiForgeryToken]
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
        try
        {
            var pdf=await reporte.GenerarServiciosAsync(true,montos,ct);
            return File(pdf,"application/pdf",montos?"Montos_agua.pdf":"Servicios_agua.pdf");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error al generar el PDF de Agua");
            TempData["Error"] = "No se pudo generar el PDF. Revise el registro de errores.";
            return RedirectToAction(nameof(Index));
        }
    }

    // Alternativa para cuando la pagina oficial no permita automatizar la consulta.
    // El usuario verifica el recibo en la pagina oficial y registra el dato visto.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarManual(long id, decimal monto, DateTime? vencimiento, CancellationToken ct)
    {
        var servicio = await db.Aguas.FindAsync(new object[] { id }, ct);
        if (servicio is null) return NotFound();
        if (!servicio.Pagar) { TempData["Error"] = "Active 'Pagar' en este servicio antes de registrar montos."; return RedirectToAction(nameof(Index)); }
        if (monto <= 0 || monto > 1000000000m)
        {
            TempData["Error"] = "Monto no valido; revise el recibo oficial.";
            return RedirectToAction(nameof(Index));
        }
        if (vencimiento.HasValue) servicio.FechaVencimiento = vencimiento.Value.Date;
        var hoy = DateTime.Today;
        var existe = await db.MontosAgua.AnyAsync(x => x.IdAgua == id && x.FechaRegistro == hoy && x.Monto == monto, ct);
        if (!existe)
            db.MontosAgua.Add(new MontoAgua { IdAgua = id, Monto = monto, FechaRegistro = hoy });
        await db.SaveChangesAsync(ct);
        TempData["Mensaje"] = existe ? "Ese monto ya estaba registrado hoy; no se duplico." : "Monto verificado manualmente y registrado.";
        return RedirectToAction(nameof(Index));
    }

}
