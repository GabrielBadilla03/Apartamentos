namespace Apartamentos.Services;

public interface IConsultaRecibosService
{
    Task<ResultadoConsulta> ConsultarAsync(string proveedor, long numeroServicio, CancellationToken ct);

    // Mantiene abierta una sola sesion de Chrome para todo el lote, como el proyecto Java.
    Task<IReadOnlyList<ResultadoConsultaLote>> ConsultarLoteAsync(
        string proveedor, IReadOnlyCollection<long> numerosServicio, CancellationToken ct);
}
