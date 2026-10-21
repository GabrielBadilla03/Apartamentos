using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class EncargadoForm
{
    public long IdEncargado { get; set; }
    [Required,StringLength(120)] public string Nombre { get; set; } = "";
    [Required,StringLength(120)] public string Apellidos { get; set; } = "";
    [Range(1,int.MaxValue)] public int Cedula { get; set; }
    public long? IdApartamento { get; set; }
    public IFormFile? Imagen { get; set; }
}
