using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

// Solo leen: no llaman a Confirmar ni publican eventos (la Fase 4 dice que una consulta no genera evento de dominio).

public sealed class ObtenerFuenteManejador : IManejadorDeConsulta<ObtenerFuenteConsulta, FuenteDto>
{
    private readonly IUnidadDeTrabajo _uow;
    public ObtenerFuenteManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<FuenteDto> Manejar(ObtenerFuenteConsulta consulta, CancellationToken ct) =>
        (await _uow.Fuentes.ObtenerORechazar(consulta.FuenteId, ct)).ADto();
}

public sealed class ListarFuentesManejador : IManejadorDeConsulta<ListarFuentesConsulta, IReadOnlyList<FuenteDto>>
{
    private readonly IUnidadDeTrabajo _uow;
    public ListarFuentesManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<IReadOnlyList<FuenteDto>> Manejar(ListarFuentesConsulta consulta, CancellationToken ct)
    {
        var fuentes = await _uow.Fuentes.Listar(ct);
        return fuentes
            .Where(f => consulta.Tipo is null || f.Tipo == consulta.Tipo)
            .Select(f => f.ADto())
            .ToList();
    }
}

public sealed class ListarVersionesManejador : IManejadorDeConsulta<ListarVersionesConsulta, IReadOnlyList<VersionResumenDto>>
{
    private readonly IUnidadDeTrabajo _uow;
    public ListarVersionesManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<IReadOnlyList<VersionResumenDto>> Manejar(ListarVersionesConsulta consulta, CancellationToken ct)
    {
        await _uow.Fuentes.ObtenerORechazar(consulta.FuenteId, ct);
        var versiones = await _uow.Versiones.Listar(consulta.FuenteId, ct);
        return versiones.OrderBy(v => v.Numero).Select(v => v.AResumen()).ToList();
    }
}

public sealed class ObtenerVersionManejador : IManejadorDeConsulta<ObtenerVersionConsulta, VersionDto>
{
    private readonly IUnidadDeTrabajo _uow;
    public ObtenerVersionManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<VersionDto> Manejar(ObtenerVersionConsulta consulta, CancellationToken ct)
    {
        await _uow.Fuentes.ObtenerORechazar(consulta.FuenteId, ct);
        var version = await _uow.Versiones.Obtener(consulta.FuenteId, consulta.Numero, ct)
            ?? throw new ErrorNoEncontrado($"La fuente no tiene la version {consulta.Numero}.");
        return version.ADto();
    }
}

public sealed class ListarCambiosManejador : IManejadorDeConsulta<ListarCambiosConsulta, IReadOnlyList<CambioDto>>
{
    private readonly IUnidadDeTrabajo _uow;
    public ListarCambiosManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<IReadOnlyList<CambioDto>> Manejar(ListarCambiosConsulta consulta, CancellationToken ct)
    {
        await _uow.Fuentes.ObtenerORechazar(consulta.FuenteId, ct);
        var cambios = await _uow.Cambios.ListarPorFuente(consulta.FuenteId, ct);
        return cambios.OrderBy(c => c.DetectadoEn).Select(c => c.ADto()).ToList();
    }
}