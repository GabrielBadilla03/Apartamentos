using Apartamentos.Data;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Services;

/// <summary>
/// Reportes PDF con codigo administrado puro. Evita cargar la biblioteca nativa de
/// QuestPDF/Skia dentro del proceso de ASP.NET mientras se diagnostica su cierre.
/// Los montos provienen de SQL Server: este reporte no hace consultas externas.
/// </summary>
public sealed class ReporteService(ApplicationDbContext db) : IReporteService
{
    public async Task<byte[]> GenerarServiciosAsync(bool agua, bool montos, CancellationToken ct)
    {
        var filas = new List<string[]>();
        if (agua)
        {
            var datos = await db.Aguas
                .Include(a => a.Apartamentos)
                .Include(a => a.MontosAgua)
                .AsSplitQuery().AsNoTracking()
                .ToListAsync(ct);
            foreach (var x in datos.Where(x => !montos || x.Pagar).OrderBy(x => x.IdAgua))
            {
                ct.ThrowIfCancellationRequested();
                filas.Add(new[] {
                    x.IdAgua.ToString(),
                    string.Join(", ", x.Apartamentos.OrderBy(a => a.DescripcionDepartamento).Select(a => a.DescripcionDepartamento)),
                    x.FechaVencimiento?.ToString("dd/MM/yyyy") ?? "",
                    x.Pagar ? "Si" : "No",
                    montos ? (x.MontosAgua.OrderByDescending(m => m.FechaRegistro)
                        .ThenByDescending(m => m.IdMonto).Select(m => m.Monto.ToString("N2"))
                        .FirstOrDefault() ?? "Sin registros") : ""
                });
            }
        }
        else
        {
            var datos = await db.Luces
                .Include(a => a.Apartamentos)
                .Include(a => a.MontosLuz)
                .AsSplitQuery().AsNoTracking()
                .ToListAsync(ct);
            foreach (var x in datos.Where(x => !montos || x.Pagar).OrderBy(x => x.IdLuz))
            {
                ct.ThrowIfCancellationRequested();
                filas.Add(new[] {
                    x.IdLuz.ToString(),
                    string.Join(", ", x.Apartamentos.OrderBy(a => a.DescripcionDepartamento).Select(a => a.DescripcionDepartamento)),
                    x.FechaVencimiento?.ToString("dd/MM/yyyy") ?? "",
                    x.Pagar ? "Si" : "No",
                    montos ? (x.MontosLuz.OrderByDescending(m => m.FechaRegistro)
                        .ThenByDescending(m => m.IdMonto).Select(m => m.Monto.ToString("N2"))
                        .FirstOrDefault() ?? "Sin registros") : ""
                });
            }
        }
        string titulo = $"Servicios de {(agua ? "Agua" : "Luz")}" +
            (montos ? " - montos registrados (no en vivo)" : "");
        return GeneradorPdfSeguro.Crear(titulo, filas);
    }
}
