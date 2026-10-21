using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace Apartamentos.Services;

public sealed class ArchivoPrivadoService(IWebHostEnvironment environment) : IArchivoPrivadoService
{
    private static readonly Dictionary<string, string> Extensiones = new(StringComparer.OrdinalIgnoreCase)
        { [".jpg"]="image/jpeg", [".jpeg"]="image/jpeg", [".png"]="image/png", [".webp"]="image/webp" };
    private string Carpeta => Path.Combine(environment.ContentRootPath,"App_Data","ImagenesPrivadas");
    public async Task<string> GuardarAsync(IFormFile archivo, CancellationToken ct)
    {
        if (archivo.Length == 0 || archivo.Length > 5*1024*1024)
            throw new InvalidOperationException("Solo se permiten imágenes entre 1 byte y 5 MB.");
        var ext = Path.GetExtension(archivo.FileName);
        if (!Extensiones.ContainsKey(ext)) throw new InvalidOperationException("Tipo de imagen no permitido.");
        Directory.CreateDirectory(Carpeta);
        var name=Guid.NewGuid().ToString("N")+ext.ToLowerInvariant();
        // No se usa nombre de fichero provisto por cliente como ruta de almacenamiento.
        var ruta=Path.Combine(Carpeta,name);
        await using var destino=new FileStream(ruta,FileMode.CreateNew,FileAccess.Write);
        await archivo.CopyToAsync(destino, ct);
        return name;
    }
    public async Task<(byte[] Contenido,string Mime)?> LeerAsync(string? referencia, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referencia)) return null;
        var name=Path.GetFileName(referencia);
        if (!string.Equals(name, referencia, StringComparison.Ordinal)) return null;
        if (!Extensiones.TryGetValue(Path.GetExtension(name),out var mime)) return null;
        var path=Path.Combine(Carpeta,name);
        if(!File.Exists(path)) return null;
        return (await File.ReadAllBytesAsync(path,ct),mime);
    }
}
