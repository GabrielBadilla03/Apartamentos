using Apartamentos.Data;
using Apartamentos.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Apartamentos.Services;

/// <summary>
/// Reproduce los reportes de Proyecto Temporal. "Servicios" muestra todos
/// los NIS sin importes. "Montos" consulta en vivo ICE/AyA para servicios
/// marcados Pagar; no reemplaza la información faltante con importes viejos.
/// </summary>
public sealed class ReporteService(
    ApplicationDbContext db,
    IConsultaRecibosService consulta,
    ILogger<ReporteService> log) : IReporteService
{
    public async Task<byte[]> GenerarServiciosAsync(bool agua, bool montos, CancellationToken ct)
    {
        var filas = new List<string[]>();
        if (agua)
        {
            var datos = await db.Aguas.Include(a => a.Apartamentos)
                .AsNoTracking().ToListAsync(ct);

            var ordenados = datos
                .Where(x => !montos || x.Pagar)
                .OrderBy(x => ClaveOrden(x.Apartamentos), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.IdAgua).ToList();

            foreach (var servicio in ordenados)
            {
                ct.ThrowIfCancellationRequested();
                var resultado = montos ? await ConsultarMonto("agua", servicio.IdAgua, ct) : null;
                filas.Add(new[]
                {
                    servicio.IdAgua.ToString(CultureInfo.InvariantCulture),
                    servicio.FechaVencimiento?.ToString("yyyy-MM-dd") ?? "",
                    TextoApartamentos(servicio.Apartamentos),
                    servicio.Pagar ? "Sí" : "No",
                    montos ? TextoMonto(resultado?.Monto) : ""
                });

                if (montos && resultado?.FechaVencimiento is DateTime nuevaFecha &&
                    nuevaFecha.Date != servicio.FechaVencimiento?.Date)
                {
                    await db.Aguas.Where(x => x.IdAgua == servicio.IdAgua)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaVencimiento, nuevaFecha.Date), ct);
                }
            }
        }
        else
        {
            var datos = await db.Luces.Include(a => a.Apartamentos)
                .AsNoTracking().ToListAsync(ct);

            var ordenados = datos
                .Where(x => !montos || x.Pagar)
                .OrderBy(x => ClaveOrden(x.Apartamentos), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.IdLuz).ToList();

            foreach (var servicio in ordenados)
            {
                ct.ThrowIfCancellationRequested();
                var resultado = montos ? await ConsultarMonto("luz", servicio.IdLuz, ct) : null;
                filas.Add(new[]
                {
                    servicio.IdLuz.ToString(CultureInfo.InvariantCulture),
                    servicio.FechaVencimiento?.ToString("yyyy-MM-dd") ?? "",
                    TextoApartamentos(servicio.Apartamentos),
                    servicio.Pagar ? "Sí" : "No",
                    montos ? TextoMonto(resultado?.Monto) : ""
                });

                if (montos && resultado?.FechaVencimiento is DateTime nuevaFecha &&
                    nuevaFecha.Date != servicio.FechaVencimiento?.Date)
                {
                    await db.Luces.Where(x => x.IdLuz == servicio.IdLuz)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaVencimiento, nuevaFecha.Date), ct);
                }
            }
        }
        return GeneradorPdfSeguro.Crear(agua ? "Listado de Agua" : "Listado de Luz", filas);
    }

    private async Task<ResultadoConsulta?> ConsultarMonto(string tipo, long nis, CancellationToken ct)
    {
        try
        {
            return await consulta.ConsultarAsync(tipo, nis, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No fue posible consultar {Tipo} NIS {Nis} para PDF", tipo, nis);
            return null;
        }
    }

    private static string TextoMonto(decimal? monto)
    {
        if (!monto.HasValue) return "No consultado";
        if (monto.Value <= 0) return "Pago";
        return monto.Value.ToString("#,##0", CultureInfo.GetCultureInfo("es-CR"));
    }

    private static IEnumerable<string> NormalizarApartamentos(IEnumerable<Apartamento> apartamentos) =>
        apartamentos.Select(a =>
        {
            string s = (a.Ubicacion ?? "") + (a.DescripcionDepartamento ?? "");
            var parentesis = s.IndexOf('(');
            if (parentesis >= 0) s = s[(parentesis + 1)..];
            return Regex.Replace(s.Replace(")", ""), @"\s+", "");
        }).Where(s => !string.IsNullOrWhiteSpace(s))
          .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

    private static string TextoApartamentos(IEnumerable<Apartamento> apartamentos) =>
        string.Join("\n", NormalizarApartamentos(apartamentos));

    private static string ClaveOrden(IEnumerable<Apartamento> apartamentos) =>
        NormalizarApartamentos(apartamentos).FirstOrDefault() ?? "";
}
