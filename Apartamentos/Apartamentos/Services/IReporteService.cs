namespace Apartamentos.Services;

public interface IReporteService
{
    Task<byte[]> GenerarServiciosAsync(bool agua, bool montos, CancellationToken ct);
}
