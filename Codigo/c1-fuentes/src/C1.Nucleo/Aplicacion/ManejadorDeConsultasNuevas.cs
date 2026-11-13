using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

// Solo leen: no confirman ni publican eventos.

// Contrato 3.2 consultarEstadoImportacion.
// Regla: SinImportar si nunca se importo; Desactualizada si hay avisos de proveedor asociados a la fuente
// recibidos DESPUES de la ultima importacion; Disponible en cualquier otro caso.
public sealed class ObtenerEstadoDeImportacionManejador
    : IManejadorDeConsulta<ObtenerEstadoDeImportacionConsulta, EstadoDeImportacionDto>
{
    private readonly IUnidadDeTrabajo _uow;
    public ObtenerEstadoDeImportacionManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<EstadoDeImportacionDto> Manejar(ObtenerEstadoDeImportacionConsulta consulta, CancellationToken ct)
    {
        var fuente = await _uow.Fuentes.ObtenerORechazar(consulta.FuenteId, ct);
        var ultima = await _uow.Versiones.ObtenerUltima(consulta.FuenteId, ct);
        var avisos = await _uow.Avisos.Listar(consulta.FuenteId, ct);

        var pendientes = avisos.Count(a => fuente.UltimaImportacionEn is null || a.RecibidoEn > fuente.UltimaImportacionEn);

        var estado = ultima is null ? "SinImportar" : pendientes > 0 ? "Desactualizada" : "Disponible";
        return new EstadoDeImportacionDto(
            fuente.Id.ToString(), estado, ultima?.Numero, fuente.UltimaImportacionEn, pendientes);
    }
}

// Adaptador A3: listar avisos
public sealed class ListarAvisosManejador : IManejadorDeConsulta<ListarAvisosConsulta, IReadOnlyList<AvisoDto>>
{
    private readonly IUnidadDeTrabajo _uow;
    public ListarAvisosManejador(IUnidadDeTrabajo uow) => _uow = uow;

    public async Task<IReadOnlyList<AvisoDto>> Manejar(ListarAvisosConsulta consulta, CancellationToken ct)
    {
        if (consulta.FuenteId is FuenteId fuenteId)
            await _uow.Fuentes.ObtenerORechazar(fuenteId, ct);

        var avisos = await _uow.Avisos.Listar(consulta.FuenteId, ct);
        return avisos.OrderBy(a => a.RecibidoEn).Select(a => a.ADto()).ToList();
    }
}