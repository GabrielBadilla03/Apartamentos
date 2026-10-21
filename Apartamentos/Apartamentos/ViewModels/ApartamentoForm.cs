using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class ApartamentoForm
{
    public long IdApartamento { get; set; }
    [Required,StringLength(160)] public string Ubicacion { get; set; } = "";
    [Required,StringLength(160)] public string DescripcionDepartamento { get; set; } = "";
    public long? IdAgua { get; set; }
    public long? IdLuz { get; set; }
}
