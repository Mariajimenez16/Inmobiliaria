using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

// SOLID:
//  - SRP: un manejador por comando. La regla de negocio vive en el dominio; aqui solo se orquesta.
//  - OCP: se agregan manejadores NUEVOS sin modificar los existentes (el Host los descubre por convencion).
//  - DIP: solo dependen de puertos.

// Contrato 4.9 marcarRevisado
public sealed class MarcarCambioRevisadoManejador : IManejadorDeComando<MarcarCambioRevisadoComando, SinResultado>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IReloj _reloj;

    public MarcarCambioRevisadoManejador(IUnidadDeTrabajo uow, IReloj reloj)
    {
        _uow = uow;
        _reloj = reloj;
    }

    public async Task<SinResultado> Manejar(MarcarCambioRevisadoComando comando, CancellationToken ct)
    {
        await _uow.Fuentes.ObtenerORechazar(comando.FuenteId, ct);

        var cambio = await _uow.Cambios.Obtener(comando.CambioId, ct);
        if (cambio is null || cambio.FuenteId != comando.FuenteId)
            throw new ErrorNoEncontrado($"La fuente no tiene el cambio '{comando.CambioId}'.");

        cambio.MarcarRevisado(comando.Usuario, _reloj.Ahora);

        await _uow.Cambios.Actualizar(cambio, ct);
        await _uow.Confirmar(ct);
        return new SinResultado();
    }
}

// Contrato 3.2 reintentarImportacion.
// SOLID - DIP/DRY: NO duplica la logica de importar; recibe el manejador de importar como dependencia y lo reutiliza.
public sealed class ReintentarImportacionManejador : IManejadorDeComando<ReintentarImportacionComando, ResultadoDeImportacion>
{
    private readonly IManejadorDeComando<ImportarMetadatosTecnicosComando, ResultadoDeImportacion> _importar;

    public ReintentarImportacionManejador(IManejadorDeComando<ImportarMetadatosTecnicosComando, ResultadoDeImportacion> importar) =>
        _importar = importar;

    public Task<ResultadoDeImportacion> Manejar(ReintentarImportacionComando comando, CancellationToken ct) =>
        _importar.Manejar(new ImportarMetadatosTecnicosComando(comando.Usuario, comando.FuenteId), ct);
}

// Adaptador A3 registrarAviso
public sealed class RegistrarAvisoDeCambioManejador : IManejadorDeComando<RegistrarAvisoDeCambioComando, Guid>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IReloj _reloj;

    public RegistrarAvisoDeCambioManejador(IUnidadDeTrabajo uow, IReloj reloj)
    {
        _uow = uow;
        _reloj = reloj;
    }

    public async Task<Guid> Manejar(RegistrarAvisoDeCambioComando comando, CancellationToken ct)
    {
        // Si el aviso dice a que fuente pertenece, esa fuente tiene que existir; si no la dice, queda pendiente de clasificar.
        if (comando.FuenteId is FuenteId fuenteId)
            await _uow.Fuentes.ObtenerORechazar(fuenteId, ct);

        var aviso = AvisoDeCambio.Registrar(comando.Proveedor, comando.Descripcion, comando.FuenteId, _reloj.Ahora);

        await _uow.Avisos.Agregar(aviso, ct);
        await _uow.Confirmar(ct);
        return aviso.Id;
    }
}