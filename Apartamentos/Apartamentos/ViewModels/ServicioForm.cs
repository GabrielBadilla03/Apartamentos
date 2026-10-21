using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class ServicioForm
{
    [Range(1,long.MaxValue,ErrorMessage="Indique un identificador válido.")]
    public long Id { get; set; }
    [Required, StringLength(160)] public string Titular { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? FechaVencimiento { get; set; }
    [DataType(DataType.Date)] public DateTime? FechaPagar { get; set; }
    [DataType(DataType.Date)] public DateTime? FechaInicioPago { get; set; }
    public bool Pagar { get; set; }
}
