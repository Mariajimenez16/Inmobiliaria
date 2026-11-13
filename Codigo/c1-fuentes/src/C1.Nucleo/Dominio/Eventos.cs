namespace C1.Nucleo.Dominio;

// Un evento es un hecho que YA ocurrio (por eso los nombres van en pasado). Son inmutables (record).
public abstract record EventoDeDominio(Guid EventoId, DateTimeOffset OcurrioEn);

// Comando Registrar fuente -> evento Fuente registrada (C2 lo consume para su copia local de fuentes)
public sealed record FuenteRegistrada(
    Guid EventoId, DateTimeOffset OcurrioEn, FuenteId FuenteId, string Nombre, TipoDeFuente Tipo)
    : EventoDeDominio(EventoId, OcurrioEn);

// Comando Autorizar acceso a metadatos -> evento Acceso a metadatos autorizado
public sealed record AccesoAMetadatosAutorizado(
    Guid EventoId, DateTimeOffset OcurrioEn, FuenteId FuenteId, Guid AutorizacionId, string Autorizador)
    : EventoDeDominio(EventoId, OcurrioEn);

// Comando Revocar autorizacion de metadatos -> evento Autorizacion de metadatos revocada
public sealed record AutorizacionDeMetadatosRevocada(
    Guid EventoId, DateTimeOffset OcurrioEn, FuenteId FuenteId, Guid AutorizacionId)
    : EventoDeDominio(EventoId, OcurrioEn);

// Comando Importar metadatos tecnicos -> evento Metadatos tecnicos importados (C6 lo consume)
public sealed record MetadatosTecnicosImportados(
    Guid EventoId, DateTimeOffset OcurrioEn, FuenteId FuenteId, Guid VersionId, int NumeroDeVersion, int CantidadDeColumnas)
    : EventoDeDominio(EventoId, OcurrioEn);

// Comando Comparar estructuras -> evento Cambio de estructura detectado, SOLO si hay diferencias (C6 lo consume)
public sealed record CambioDeEstructuraDetectado(
    Guid EventoId, DateTimeOffset OcurrioEn, FuenteId FuenteId, Guid CambioId,
    int VersionAnterior, int VersionNueva, int CantidadDeDiferencias)
    : EventoDeDominio(EventoId, OcurrioEn);