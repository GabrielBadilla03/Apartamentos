using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Models;

public class Luz
{
    [Key] public long IdLuz { get; set; } // NIS, manual, NO identity
    [Required, MaxLength(160)] public string Titular { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? FechaVencimiento { get; set; }
    [DataType(DataType.Date)] public DateTime? FechaPagar { get; set; }
    [DataType(DataType.Date)] public DateTime? FechaInicioPago { get; set; }
    public bool Pagar { get; set; }
    public ICollection<Apartamento> Apartamentos { get; set; } = new List<Apartamento>();
    public ICollection<MontoLuz> MontosLuz { get; set; } = new List<MontoLuz>();
}
