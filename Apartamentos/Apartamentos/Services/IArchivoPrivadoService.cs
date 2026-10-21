using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace Apartamentos.Services;

public interface IArchivoPrivadoService
{
    Task<string> GuardarAsync(IFormFile archivo, CancellationToken ct);
    Task<(byte[] Contenido,string Mime)?> LeerAsync(string? referencia, CancellationToken ct);
}
