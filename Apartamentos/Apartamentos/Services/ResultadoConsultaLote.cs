namespace Apartamentos.Services;

/// <summary>Resultado individual de una consulta en lote; una falla no detiene las restantes.</summary>
public sealed record ResultadoConsultaLote(
    long NumeroServicio,
    ResultadoConsulta? Resultado,
    string? Error);
