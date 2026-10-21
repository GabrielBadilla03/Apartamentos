using Apartamentos.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Apartamentos.Services;

public sealed class ReporteService(ApplicationDbContext db) : IReporteService
{
    public async Task<byte[]> GenerarServiciosAsync(bool agua,bool montos,CancellationToken ct)
    {
        var filas=new List<(string Id,string Apartamentos,string Vencimiento,string Pagar,string Monto)>();
        if(agua)
        {
            var datos=await db.Aguas.Include(x=>x.Apartamentos).Include(x=>x.MontosAgua).AsNoTracking().ToListAsync(ct);
            foreach(var x in datos.Where(x=>!montos||x.Pagar))
                filas.Add((x.IdAgua.ToString(),string.Join(", ",x.Apartamentos.OrderBy(a=>a.DescripcionDepartamento).Select(a=>a.DescripcionDepartamento)),x.FechaVencimiento?.ToString("dd/MM/yyyy")??"",x.Pagar?"Sí":"No",montos?x.MontosAgua.OrderByDescending(m=>m.FechaRegistro).Select(m=>m.Monto.ToString("N2")).FirstOrDefault()??"Sin registros":""));
        }
        else
        {
            var datos=await db.Luces.Include(x=>x.Apartamentos).Include(x=>x.MontosLuz).AsNoTracking().ToListAsync(ct);
            foreach(var x in datos.Where(x=>!montos||x.Pagar))
                filas.Add((x.IdLuz.ToString(),string.Join(", ",x.Apartamentos.OrderBy(a=>a.DescripcionDepartamento).Select(a=>a.DescripcionDepartamento)),x.FechaVencimiento?.ToString("dd/MM/yyyy")??"",x.Pagar?"Sí":"No",montos?x.MontosLuz.OrderByDescending(m=>m.FechaRegistro).Select(m=>m.Monto.ToString("N2")).FirstOrDefault()??"Sin registros":""));
        }
        return Document.Create(doc=>doc.Page(page=>
        {
            page.Size(PageSizes.A4);page.Margin(25);page.DefaultTextStyle(t=>t.FontSize(9));
            page.Header().Text($"Listado de {(agua?"Agua":"Luz")}{(montos?" - Montos registrados":"")}").FontSize(16).Bold();
            page.Content().Table(table=>
            {
                table.ColumnsDefinition(c=>{ c.ConstantColumn(70);c.RelativeColumn(3);c.ConstantColumn(80);c.ConstantColumn(48);c.ConstantColumn(72); });
                table.Header(h=>{foreach(var head in new[]{"ID","Apartamentos","Vencimiento","Pagar","Monto"})h.Cell().BorderBottom(1).Padding(3).Text(head).Bold();});
                foreach(var row in filas)
                foreach(var val in new[]{row.Id,row.Apartamentos,row.Vencimiento,row.Pagar,row.Monto})
                    table.Cell().BorderBottom(0.4f).Padding(3).Text(val);
            });
            page.Footer().AlignRight().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}");
        })).GeneratePdf();
    }
}
