namespace Apartamentos.Services;

public interface IConsultaRecibosService
{
    Task<ResultadoConsulta> ConsultarAsync(string proveedor, long numeroServicio, CancellationToken ct);
}
