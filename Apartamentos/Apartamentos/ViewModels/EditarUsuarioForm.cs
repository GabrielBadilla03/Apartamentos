using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Apartamentos.ViewModels;

public sealed class EditarUsuarioForm
{
    [Required] public string Id { get; set; } = "";
    [Required,EmailAddress] public string Email { get; set; } = "";
    [Required,MaxLength(100)] public string Nombre { get; set; } = "";
    [Required,MaxLength(100)] public string Apellidos { get; set; } = "";
    [Phone] public string? Telefono { get; set; }
    public bool Activo { get; set; }

    public IFormFile? Imagen { get; set; }
}
