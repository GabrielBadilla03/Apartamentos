using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Models;

public class MontoAgua
{
    [Key] public long IdMonto { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Monto { get; set; }
    [DataType(DataType.Date)] public DateTime FechaRegistro { get; set; } = DateTime.Today;
    public long? IdAgua { get; set; }
    public Agua? Agua { get; set; }
}
