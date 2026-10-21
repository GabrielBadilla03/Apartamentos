using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Models;

public class Apartamento
{
    [Key] public long IdApartamento { get; set; }
    [Required, MaxLength(160)] public string Ubicacion { get; set; } = "";
    [Required, MaxLength(160)] public string DescripcionDepartamento { get; set; } = "";
    public long? IdAgua { get; set; }
    public Agua? Agua { get; set; }
    public long? IdLuz { get; set; }
    public Luz? Luz { get; set; }
    public ICollection<Encargado> Encargados { get; set; } = new List<Encargado>();
}
