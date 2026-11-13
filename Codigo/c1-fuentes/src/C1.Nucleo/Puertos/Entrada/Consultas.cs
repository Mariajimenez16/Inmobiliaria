using C1.Nucleo.Dominio;

namespace C1.Nucleo.Puertos.Entrada;

public sealed record ObtenerFuenteConsulta(FuenteId FuenteId) : IConsulta<FuenteDto>;

public sealed record ListarFuentesConsulta(TipoDeFuente? Tipo = null) : IConsulta<IReadOnlyList<FuenteDto>>;

public sealed record ListarVersionesConsulta(FuenteId FuenteId) : IConsulta<IReadOnlyList<VersionResumenDto>>;

public sealed record ObtenerVersionConsulta(FuenteId FuenteId, int Numero) : IConsulta<VersionDto>;

public sealed record ListarCambiosConsulta(FuenteId FuenteId) : IConsulta<IReadOnlyList<CambioDto>>;

// Contrato 3.2 consultarEstadoImportacion
public sealed record ObtenerEstadoDeImportacionConsulta(FuenteId FuenteId) : IConsulta<EstadoDeImportacionDto>;

// Adaptador A3: lista los avisos (todos, o los de una fuente)
public sealed record ListarAvisosConsulta(FuenteId? FuenteId = null) : IConsulta<IReadOnlyList<AvisoDto>>;