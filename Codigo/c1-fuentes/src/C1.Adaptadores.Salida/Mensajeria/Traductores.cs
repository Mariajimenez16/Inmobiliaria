using C1.Contracts.V1;
using C1.Nucleo.Dominio;

namespace C1.Adaptadores.Salida.Mensajeria;

// CAPA ANTICORRUPCION de salida (diapositiva 17): cada traductor convierte UN evento de dominio en UN contrato publicado (V1).
// El modelo interno no sale. Si cambia un contrato, cambia solo su traductor.
public interface ITraductorDeEvento
{
    bool Atiende(EventoDeDominio evento);
    MensajeSalida Traducir(EventoDeDominio evento);
}

// Base comun: evita repetir el "if es de mi tipo" y el casting en cada traductor (DRY), sin mezclar responsabilidades.
public abstract class TraductorDeEvento<TEvento> : ITraductorDeEvento where TEvento : EventoDeDominio
{
    public bool Atiende(EventoDeDominio evento) => evento is TEvento;

    public MensajeSalida Traducir(EventoDeDominio evento) => Convertir((TEvento)evento);

    protected abstract MensajeSalida Convertir(TEvento evento);

    protected static string Id(EventoDeDominio e) => e.EventoId.ToString();

    protected static string Fecha(EventoDeDominio e) => e.OcurrioEn.ToString("o");
}

public sealed class FuenteRegistradaTraductor : TraductorDeEvento<FuenteRegistrada>
{
    protected override MensajeSalida Convertir(FuenteRegistrada e) =>
        new(FuenteRegistradaV1.Tipo, new FuenteRegistradaV1(Id(e), e.FuenteId.ToString(), e.Nombre, Fecha(e)));
}

public sealed class AccesoAMetadatosAutorizadoTraductor : TraductorDeEvento<AccesoAMetadatosAutorizado>
{
    protected override MensajeSalida Convertir(AccesoAMetadatosAutorizado e) =>
        new(AccesoAMetadatosAutorizadoV1.Tipo,
            new AccesoAMetadatosAutorizadoV1(Id(e), e.FuenteId.ToString(), e.AutorizacionId.ToString(), e.Autorizador, Fecha(e)));
}

public sealed class AutorizacionDeMetadatosRevocadaTraductor : TraductorDeEvento<AutorizacionDeMetadatosRevocada>
{
    protected override MensajeSalida Convertir(AutorizacionDeMetadatosRevocada e) =>
        new(AutorizacionDeMetadatosRevocadaV1.Tipo,
            new AutorizacionDeMetadatosRevocadaV1(Id(e), e.FuenteId.ToString(), e.AutorizacionId.ToString(), Fecha(e)));
}

public sealed class MetadatosTecnicosImportadosTraductor : TraductorDeEvento<MetadatosTecnicosImportados>
{
    protected override MensajeSalida Convertir(MetadatosTecnicosImportados e) =>
        new(MetadatosTecnicosImportadosV1.Tipo,
            new MetadatosTecnicosImportadosV1(Id(e), e.FuenteId.ToString(), e.VersionId.ToString(),
                e.NumeroDeVersion, e.CantidadDeColumnas, Fecha(e)));
}

public sealed class CambioDeEstructuraDetectadoTraductor : TraductorDeEvento<CambioDeEstructuraDetectado>
{
    protected override MensajeSalida Convertir(CambioDeEstructuraDetectado e) =>
        new(CambioDeEstructuraDetectadoV1.Tipo,
            new CambioDeEstructuraDetectadoV1(Id(e), e.FuenteId.ToString(), e.CambioId.ToString(),
                e.VersionAnterior, e.VersionNueva, e.CantidadDeDiferencias, Fecha(e)));
}

// Lista de traductores para la raiz de composicion (el Host) y las pruebas. La diapositiva 17 indica que se registran explicitamente.
public static class TraductoresDeEventos
{
    public static IReadOnlyList<ITraductorDeEvento> Todos() => new ITraductorDeEvento[]
    {
        new FuenteRegistradaTraductor(),
        new AccesoAMetadatosAutorizadoTraductor(),
        new AutorizacionDeMetadatosRevocadaTraductor(),
        new MetadatosTecnicosImportadosTraductor(),
        new CambioDeEstructuraDetectadoTraductor()
    };
}