using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class CrearUsuarioForm
{
    [Required,EmailAddress] public string Email { get; set; } = "";
    [Required,MinLength(8)] public string Password { get; set; } = "";
    [Required,MaxLength(100)] public string Nombre { get; set; } = "";
    [Required,MaxLength(100)] public string Apellidos { get; set; } = "";
    [Phone] public string? Telefono { get; set; }

    public IFormFile? Imagen { get; set; }
}
