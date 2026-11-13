using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;

namespace C1.Nucleo.Aplicacion;

// SOLID - SRP: solo traduce dominio -> DTO. Asi el dominio no se "filtra" hacia afuera.
internal static class Mapeos
{
    public static FuenteDto ADto(this Fuente f) => new(
        f.Id.ToString(), f.Nombre, f.Tipo.ToString(), f.RegistradaEn, f.UltimaImportacionEn, f.SinCubrir,
        f.AutorizacionVigente is not null,
        f.Autorizaciones.Select(a => new AutorizacionDto(a.Id, a.Autorizador, a.AutorizadaEn, a.RevocadaEn, a.Vigente)).ToList());

    public static VersionResumenDto AResumen(this VersionDeEstructura v) =>
        new(v.Id, v.Numero, v.ImportadaEn, v.Columnas.Count);

    public static VersionDto ADto(this VersionDeEstructura v) => new(
        v.Id, v.FuenteId.ToString(), v.Numero, v.ImportadaEn,
        v.Columnas.Select(c => new MetadatoTecnicoDto(c.Tabla, c.Columna, c.TipoDeDato, c.Longitud, c.Obligatorio)).ToList());

    public static DiferenciaDto ADto(this DiferenciaDeEstructura d) => new(d.Tipo.ToString(), d.Tabla, d.Columna, d.Detalle);

        public static CambioDto ADto(this CambioDeEstructura c) => new(
        c.Id, c.FuenteId.ToString(), c.VersionAnterior, c.VersionNueva, c.DetectadoEn,
        c.Diferencias.Select(d => d.ADto()).ToList(),
        c.Revisado, c.RevisadoPor, c.RevisadoEn);

    public static AvisoDto ADto(this AvisoDeCambio a) => new(
        a.Id, a.Proveedor, a.Descripcion, a.FuenteId?.ToString(), a.Estado.ToString(), a.RecibidoEn);

}