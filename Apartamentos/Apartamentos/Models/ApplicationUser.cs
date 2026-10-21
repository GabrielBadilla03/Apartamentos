using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Models;

public sealed class ApplicationUser : IdentityUser
{
    [MaxLength(100)] public string Nombre { get; set; } = "";
    [MaxLength(100)] public string Apellidos { get; set; } = "";
    [MaxLength(4096)] public string? RutaImagen { get; set; }
    public bool Activo { get; set; } = true;
    public long? IdUsuarioAnterior { get; set; }
}
