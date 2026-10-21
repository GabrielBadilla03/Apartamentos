using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class MontoForm
{
    public long IdMonto { get; set; }
    [Range(0.01,double.MaxValue)] public decimal Monto { get; set; }
    public long? IdServicio { get; set; }
    [DataType(DataType.Date)] public DateTime FechaRegistro { get; set; } = DateTime.Today;
}
