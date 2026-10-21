using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Apartamentos.Models;

public class Encargado
{
    [Key] public long IdEncargado { get; set; }
    [Required, MaxLength(120)] public string Nombre { get; set; } = "";
    [Required, MaxLength(120)] public string Apellidos { get; set; } = "";
    // Se conserva int para facilitar importación literal de la base heredada.
    public int Cedula { get; set; }
    [MaxLength(4096)] public string? FotoCedula { get; set; }
    public long? IdApartamento { get; set; }
    public Apartamento? Apartamento { get; set; }
}
