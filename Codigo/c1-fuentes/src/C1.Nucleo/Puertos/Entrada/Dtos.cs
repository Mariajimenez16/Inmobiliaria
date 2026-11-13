namespace C1.Nucleo.Puertos.Entrada;

public sealed record AutorizacionDto(Guid Id, string Autorizador, DateTimeOffset AutorizadaEn, DateTimeOffset? RevocadaEn, bool Vigente);

public sealed record FuenteDto(
    string Id, string Nombre, string Tipo, DateTimeOffset RegistradaEn, DateTimeOffset? UltimaImportacionEn,
    bool SinCubrir, bool ImportacionHabilitada, IReadOnlyList<AutorizacionDto> Autorizaciones);

public sealed record MetadatoTecnicoDto(string Tabla, string Columna, string TipoDeDato, int? Longitud, bool Obligatorio);

public sealed record VersionResumenDto(Guid Id, int Numero, DateTimeOffset ImportadaEn, int CantidadDeColumnas);

public sealed record VersionDto(Guid Id, string FuenteId, int Numero, DateTimeOffset ImportadaEn, IReadOnlyList<MetadatoTecnicoDto> Columnas);

public sealed record DiferenciaDto(string Tipo, string Tabla, string Columna, string Detalle);

public sealed record CambioDto(
    Guid Id, string FuenteId, int VersionAnterior, int VersionNueva, DateTimeOffset DetectadoEn,
    IReadOnlyList<DiferenciaDto> Diferencias,
    bool Revisado = false, string? RevisadoPor = null, DateTimeOffset? RevisadoEn = null);

public sealed record ResultadoDeImportacion(Guid VersionId, int NumeroDeVersion, int CantidadDeColumnas);

public sealed record ResultadoDeComparacion(bool HayCambio, Guid? CambioId, IReadOnlyList<DiferenciaDto> Diferencias);

// Estado: "SinImportar" | "Disponible" | "Desactualizada"
public sealed record EstadoDeImportacionDto(
    string FuenteId, string Estado, int? UltimaVersion, DateTimeOffset? UltimaImportacionEn, int AvisosPendientes);

public sealed record AvisoDto(
    Guid Id, string Proveedor, string Descripcion, string? FuenteId, string Estado, DateTimeOffset RecibidoEn);