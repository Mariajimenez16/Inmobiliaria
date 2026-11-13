using System.Text.Json.Serialization;

namespace C1.Contracts.V1;

// LENGUAJE PUBLICADO de C1 (V8, relacion 2: Open Host Service + Published Language).
// Solo tipos primitivos y nombres en snake_case, igual que los contratos de C2. El sufijo ".v1" permite evolucionar sin romper a los consumidores.

// Consumido por C2 (copia local de identidades de fuente).
public sealed record FuenteRegistradaV1(
    [property: JsonPropertyName("evento_id")] string EventoId,
    [property: JsonPropertyName("fuente_id")] string FuenteId,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("ocurrio_en")] string OcurrioEn)
{
    public const string Tipo = "FuenteRegistrada.v1";
}

// Sin consumidor asignado en el diseno.
public sealed record AccesoAMetadatosAutorizadoV1(
    [property: JsonPropertyName("evento_id")] string EventoId,
    [property: JsonPropertyName("fuente_id")] string FuenteId,
    [property: JsonPropertyName("autorizacion_id")] string AutorizacionId,
    [property: JsonPropertyName("autorizador")] string Autorizador,
    [property: JsonPropertyName("ocurrio_en")] string OcurrioEn)
{
    public const string Tipo = "AccesoAMetadatosAutorizado.v1";
}

// Sin consumidor asignado en el diseno.
public sealed record AutorizacionDeMetadatosRevocadaV1(
    [property: JsonPropertyName("evento_id")] string EventoId,
    [property: JsonPropertyName("fuente_id")] string FuenteId,
    [property: JsonPropertyName("autorizacion_id")] string AutorizacionId,
    [property: JsonPropertyName("ocurrio_en")] string OcurrioEn)
{
    public const string Tipo = "AutorizacionDeMetadatosRevocada.v1";
}

// Consumido por C6 (V8, relacion 9).
public sealed record MetadatosTecnicosImportadosV1(
    [property: JsonPropertyName("evento_id")] string EventoId,
    [property: JsonPropertyName("fuente_id")] string FuenteId,
    [property: JsonPropertyName("version_id")] string VersionId,
    [property: JsonPropertyName("numero_de_version")] int NumeroDeVersion,
    [property: JsonPropertyName("cantidad_de_columnas")] int CantidadDeColumnas,
    [property: JsonPropertyName("ocurrio_en")] string OcurrioEn)
{
    public const string Tipo = "MetadatosTecnicosImportados.v1";
}

// Consumido por C6 (V8, relacion 9).
public sealed record CambioDeEstructuraDetectadoV1(
    [property: JsonPropertyName("evento_id")] string EventoId,
    [property: JsonPropertyName("fuente_id")] string FuenteId,
    [property: JsonPropertyName("cambio_id")] string CambioId,
    [property: JsonPropertyName("version_anterior")] int VersionAnterior,
    [property: JsonPropertyName("version_nueva")] int VersionNueva,
    [property: JsonPropertyName("cantidad_de_diferencias")] int CantidadDeDiferencias,
    [property: JsonPropertyName("ocurrio_en")] string OcurrioEn)
{
    public const string Tipo = "CambioDeEstructuraDetectado.v1";
}