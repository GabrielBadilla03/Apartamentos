namespace Apartamentos.Services;

public interface IConsultaRecibosService
{
    Task<ResultadoConsulta> ConsultarAsync(string proveedor, long numeroServicio, CancellationToken ct);

    // Mantiene abierta una sola sesion de Chrome para todo el lote, como el proyecto Java.
    Task<IReadOnlyList<ResultadoConsultaLote>> ConsultarLoteAsync(
        string proveedor, IReadOnlyCollection<long> numerosServicio, CancellationToken ct);

    // Entrega cada resultado tan pronto se obtiene, para guardar montos de PDF inmediatamente.
    IAsyncEnumerable<ResultadoConsultaLote> ConsultarLoteProgresivoAsync(
        string proveedor, IReadOnlyCollection<long> numerosServicio, CancellationToken ct = default);
}
