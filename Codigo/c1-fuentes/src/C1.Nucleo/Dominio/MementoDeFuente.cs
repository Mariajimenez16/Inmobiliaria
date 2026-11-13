namespace C1.Nucleo.Dominio;

// Foto inmutable del estado que CAMBIA en una Fuente (nombre, tipo, id y fecha de registro nunca cambian).
// Solo datos, sin logica. Las autorizaciones se copian como valores, no como referencias: si la fuente se modifica despues,
// la foto no se altera.
public sealed record AutorizacionGuardada(
    Guid Id, FuenteId FuenteId, string Autorizador, DateTimeOffset AutorizadaEn, DateTimeOffset? RevocadaEn);

public sealed record MementoDeFuente(
    FuenteId FuenteId,
    DateTimeOffset? UltimaImportacionEn,
    IReadOnlyList<AutorizacionGuardada> Autorizaciones,
    int EventosPendientes);