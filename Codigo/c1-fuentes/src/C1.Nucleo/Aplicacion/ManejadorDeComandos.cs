using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

// IMPLEMENTAN los puertos de entrada (un manejador por comando).
// SOLID:
//  - SRP: cada clase atiende UN comando.
//  - DIP: solo conocen interfaces (puertos); nada de SQLite ni de HTTP.
//  - La logica de negocio NO esta aqui: esta en el dominio. Aqui solo se orquesta: cargar -> llamar al dominio -> guardar -> publicar.

public sealed class RegistrarFuenteManejador : IManejadorDeComando<RegistrarFuenteComando, FuenteId>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IPublicadorDeEventos _publicador;
    private readonly IReloj _reloj;

    public RegistrarFuenteManejador(IUnidadDeTrabajo uow, IPublicadorDeEventos publicador, IReloj reloj)
    {
        _uow = uow;
        _publicador = publicador;
        _reloj = reloj;
    }

    public async Task<FuenteId> Manejar(RegistrarFuenteComando comando, CancellationToken ct)
    {
        var fuente = Fuente.Registrar(comando.Nombre, comando.Tipo, _reloj.Ahora);

        await _uow.Fuentes.Agregar(fuente, ct);
        if (!string.IsNullOrWhiteSpace(comando.Credenciales))
            await _uow.Credenciales.Guardar(fuente.Id, comando.Credenciales, ct);
        await _uow.Confirmar(ct);

        await _publicador.Publicar(fuente.ExtraerEventos(), ct);
        return fuente.Id;
    }
}

public sealed class AutorizarAccesoAMetadatosManejador : IManejadorDeComando<AutorizarAccesoAMetadatosComando, Guid>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IPublicadorDeEventos _publicador;
    private readonly IReloj _reloj;

    public AutorizarAccesoAMetadatosManejador(IUnidadDeTrabajo uow, IPublicadorDeEventos publicador, IReloj reloj)
    {
        _uow = uow;
        _publicador = publicador;
        _reloj = reloj;
    }

    public async Task<Guid> Manejar(AutorizarAccesoAMetadatosComando comando, CancellationToken ct)
    {
        var fuente = await _uow.Fuentes.ObtenerORechazar(comando.FuenteId, ct);
        var autorizacion = fuente.AutorizarAcceso(comando.Autorizador, _reloj.Ahora);

        await _uow.Fuentes.Actualizar(fuente, ct);
        await _uow.Confirmar(ct);

        await _publicador.Publicar(fuente.ExtraerEventos(), ct);
        return autorizacion.Id;
    }
}

public sealed class RevocarAutorizacionDeMetadatosManejador : IManejadorDeComando<RevocarAutorizacionDeMetadatosComando, SinResultado>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IPublicadorDeEventos _publicador;
    private readonly IReloj _reloj;

    public RevocarAutorizacionDeMetadatosManejador(IUnidadDeTrabajo uow, IPublicadorDeEventos publicador, IReloj reloj)
    {
        _uow = uow;
        _publicador = publicador;
        _reloj = reloj;
    }

    public async Task<SinResultado> Manejar(RevocarAutorizacionDeMetadatosComando comando, CancellationToken ct)
    {
        var fuente = await _uow.Fuentes.ObtenerORechazar(comando.FuenteId, ct);
        fuente.RevocarAutorizacion(_reloj.Ahora);

        await _uow.Fuentes.Actualizar(fuente, ct);
        await _uow.Confirmar(ct);

        await _publicador.Publicar(fuente.ExtraerEventos(), ct);
        return new SinResultado();
    }
}

public sealed class ImportarMetadatosTecnicosManejador : IManejadorDeComando<ImportarMetadatosTecnicosComando, ResultadoDeImportacion>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IEnumerable<ILectorDeEstructura> _lectores;
    private readonly IPublicadorDeEventos _publicador;
    private readonly IReloj _reloj;

    public ImportarMetadatosTecnicosManejador(
        IUnidadDeTrabajo uow, IEnumerable<ILectorDeEstructura> lectores, IPublicadorDeEventos publicador, IReloj reloj)
    {
        _uow = uow;
        _lectores = lectores;
        _publicador = publicador;
        _reloj = reloj;
    }

    public async Task<ResultadoDeImportacion> Manejar(ImportarMetadatosTecnicosComando comando, CancellationToken ct)
    {
        var fuente = await _uow.Fuentes.ObtenerORechazar(comando.FuenteId, ct);

        // Invariante de la V6.a: sin autorizacion vigente NO se toca la fuente externa.
        fuente.VerificarImportacionPermitida();

        // OCP: el lector se elige por tipo de fuente. Agregar otro tipo = agregar otro lector, sin tocar este caso de uso.
        var lector = _lectores.FirstOrDefault(l => l.Atiende(fuente.Tipo))
            ?? throw new ErrorDeReglaDeNegocio(
                $"Las fuentes de tipo '{fuente.Tipo}' no tienen diccionario tecnico que importar.");

        var credenciales = await _uow.Credenciales.Obtener(fuente.Id, ct);
        var ultima = await _uow.Versiones.ObtenerUltima(fuente.Id, ct);

        IReadOnlyList<MetadatoTecnico> columnas;
        try
        {
            columnas = await lector.LeerEstructura(fuente, credenciales, ct);
        }
        catch (ErrorDeFuenteNoDisponible ex)
        {
            // V4d: si la fuente no responde, se conserva la ultima version conocida y se muestra su fecha.
            var nota = ultima is null
                ? "Todavia no hay una version conocida de esta fuente."
                : $"Se conserva la ultima version conocida: v{ultima.Numero}, importada el {ultima.ImportadaEn:yyyy-MM-dd HH:mm} UTC.";
            throw new ErrorDeFuenteNoDisponible($"{ex.Detalle} {nota}");
        }

            var numero = (ultima?.Numero ?? 0) + 1;
            var version = VersionDeEstructura.Crear(fuente.Id, numero, _reloj.Ahora, columnas);

            // Patron Memento: este manejador es el CARETAKER. Guarda la foto antes de modificar la fuente y, si algo falla
            // al persistir, la restaura para no dejar el agregado a medias (la base de datos ya hace su propio rollback).
            var memento = fuente.CrearMemento();
            try
            {
                fuente.RegistrarImportacion(version.Id, numero, version.Columnas.Count, _reloj.Ahora);

                await _uow.Versiones.Agregar(version, ct);
                await _uow.Fuentes.Actualizar(fuente, ct);
                await _uow.Confirmar(ct);
            }
            catch
            {
                fuente.Restaurar(memento);
                throw;
            }

        await _publicador.Publicar(fuente.ExtraerEventos(), ct);
        return new ResultadoDeImportacion(version.Id, numero, version.Columnas.Count);
    }
}

public sealed class CompararEstructurasManejador : IManejadorDeComando<CompararEstructurasComando, ResultadoDeComparacion>
{
    private readonly IUnidadDeTrabajo _uow;
    private readonly IPublicadorDeEventos _publicador;
    private readonly IReloj _reloj;

    public CompararEstructurasManejador(IUnidadDeTrabajo uow, IPublicadorDeEventos publicador, IReloj reloj)
    {
        _uow = uow;
        _publicador = publicador;
        _reloj = reloj;
    }

    public async Task<ResultadoDeComparacion> Manejar(CompararEstructurasComando comando, CancellationToken ct)
    {
        await _uow.Fuentes.ObtenerORechazar(comando.FuenteId, ct);

        if (comando.VersionAnterior.HasValue != comando.VersionNueva.HasValue)
            throw new ErrorDeValidacion("Indique las dos versiones a comparar, o ninguna para comparar las dos mas recientes.");

        VersionDeEstructura anterior;
        VersionDeEstructura nueva;

        if (comando.VersionAnterior is int numeroAnterior && comando.VersionNueva is int numeroNuevo)
        {
            anterior = await _uow.Versiones.Obtener(comando.FuenteId, numeroAnterior, ct)
                ?? throw new ErrorNoEncontrado($"La fuente no tiene la version {numeroAnterior}.");
            nueva = await _uow.Versiones.Obtener(comando.FuenteId, numeroNuevo, ct)
                ?? throw new ErrorNoEncontrado($"La fuente no tiene la version {numeroNuevo}.");
        }
        else
        {
            var versiones = (await _uow.Versiones.Listar(comando.FuenteId, ct)).OrderBy(v => v.Numero).ToList();
            if (versiones.Count < 2)
                throw new ErrorDeReglaDeNegocio("Se necesitan al menos dos versiones importadas para comparar estructuras.");
            anterior = versiones[^2];
            nueva = versiones[^1];
        }

        // Si no hay diferencias, el dominio devuelve null: no se guarda nada y NO se publica ningun evento.
        var cambio = CambioDeEstructura.Detectar(anterior, nueva, _reloj.Ahora);
        if (cambio is null)
            return new ResultadoDeComparacion(false, null, Array.Empty<DiferenciaDto>());

        await _uow.Cambios.Agregar(cambio, ct);
        await _uow.Confirmar(ct);

        await _publicador.Publicar(cambio.ExtraerEventos(), ct);
        return new ResultadoDeComparacion(true, cambio.Id, cambio.Diferencias.Select(d => d.ADto()).ToList());
    }
}