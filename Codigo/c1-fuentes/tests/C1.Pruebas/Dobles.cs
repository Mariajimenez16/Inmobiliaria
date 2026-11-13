using C1.Nucleo.Aplicacion;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Pruebas;

internal sealed class RelojFijo : IReloj
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class RepositorioDeFuentesEnMemoria : IRepositorioDeFuentes
{
    public Dictionary<FuenteId, Fuente> Datos { get; } = new();

    public Task<Fuente?> Obtener(FuenteId id, CancellationToken ct) =>
        Task.FromResult<Fuente?>(Datos.TryGetValue(id, out var fuente) ? fuente : null);

    public Task<IReadOnlyList<Fuente>> Listar(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Fuente>>(Datos.Values.ToList());

    public Task Agregar(Fuente fuente, CancellationToken ct)
    {
        Datos[fuente.Id] = fuente;
        return Task.CompletedTask;
    }

    public Task Actualizar(Fuente fuente, CancellationToken ct)
    {
        Datos[fuente.Id] = fuente;
        return Task.CompletedTask;
    }
}

internal sealed class RepositorioDeVersionesEnMemoria : IRepositorioDeVersiones
{
    public List<VersionDeEstructura> Datos { get; } = new();

    public Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct) =>
        Task.FromResult(Datos.Where(v => v.FuenteId == fuenteId).OrderByDescending(v => v.Numero).FirstOrDefault());

    public Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct) =>
        Task.FromResult(Datos.FirstOrDefault(v => v.FuenteId == fuenteId && v.Numero == numero));

    public Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VersionDeEstructura>>(Datos.Where(v => v.FuenteId == fuenteId).ToList());

    public Task Agregar(VersionDeEstructura version, CancellationToken ct)
    {
        Datos.Add(version);
        return Task.CompletedTask;
    }
}

internal sealed class RepositorioDeCambiosEnMemoria : IRepositorioDeCambios
{
    public List<CambioDeEstructura> Datos { get; } = new();

    public Task Agregar(CambioDeEstructura cambio, CancellationToken ct)
    {
        Datos.Add(cambio);
        return Task.CompletedTask;
    }

    public Task<CambioDeEstructura?> Obtener(Guid cambioId, CancellationToken ct) =>
        Task.FromResult(Datos.FirstOrDefault(c => c.Id == cambioId));

    public Task Actualizar(CambioDeEstructura cambio, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<CambioDeEstructura>> ListarPorFuente(FuenteId fuenteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CambioDeEstructura>>(Datos.Where(c => c.FuenteId == fuenteId).ToList());
}

internal sealed class RepositorioDeAvisosEnMemoria : IRepositorioDeAvisos
{
    public List<AvisoDeCambio> Datos { get; } = new();

    public Task Agregar(AvisoDeCambio aviso, CancellationToken ct)
    {
        Datos.Add(aviso);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvisoDeCambio>> Listar(FuenteId? fuenteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AvisoDeCambio>>(
            Datos.Where(a => fuenteId is null || a.FuenteId == fuenteId).ToList());
}

internal sealed class AlmacenDeCredencialesEnMemoria : IAlmacenDeCredenciales
{
    public Dictionary<FuenteId, string> Datos { get; } = new();

    public Task Guardar(FuenteId fuenteId, string credenciales, CancellationToken ct)
    {
        Datos[fuenteId] = credenciales;
        return Task.CompletedTask;
    }

    public Task<string?> Obtener(FuenteId fuenteId, CancellationToken ct) =>
        Task.FromResult<string?>(Datos.TryGetValue(fuenteId, out var valor) ? valor : null);
}

internal sealed class UnidadDeTrabajoEnMemoria : IUnidadDeTrabajo
{
    public RepositorioDeFuentesEnMemoria FuentesMem { get; } = new();
    public RepositorioDeVersionesEnMemoria VersionesMem { get; } = new();
    public RepositorioDeCambiosEnMemoria CambiosMem { get; } = new();
    public AlmacenDeCredencialesEnMemoria CredencialesMem { get; } = new();
    public int Confirmaciones { get; private set; }

    public IRepositorioDeFuentes Fuentes => FuentesMem;
    public IRepositorioDeVersiones? VersionesSustituto { get; set; }   // permite simular que el repositorio falla
    public IRepositorioDeVersiones Versiones => VersionesSustituto ?? VersionesMem;    public IRepositorioDeCambios Cambios => CambiosMem;
    public IAlmacenDeCredenciales Credenciales => CredencialesMem;
    public RepositorioDeAvisosEnMemoria AvisosMem { get; } = new();
    public IRepositorioDeAvisos Avisos => AvisosMem;

    public Task Confirmar(CancellationToken ct)
    {
        Confirmaciones++;
        return Task.CompletedTask;
    }
}

internal sealed class LectorFalso : ILectorDeEstructura
{
    public bool Disponible { get; set; } = true;
    public int Lecturas { get; private set; }
    public string? UltimasCredenciales { get; private set; }

    public IReadOnlyList<MetadatoTecnico> Estructura { get; set; } = new[]
    {
        new MetadatoTecnico("clientes", "cedula", "varchar", 20, true),
        new MetadatoTecnico("clientes", "nombre", "varchar", 100, true)
    };

    public bool Atiende(TipoDeFuente tipo) => tipo == TipoDeFuente.MotorRelacional;

    public Task<IReadOnlyList<MetadatoTecnico>> LeerEstructura(Fuente fuente, string? credenciales, CancellationToken ct)
    {
        Lecturas++;
        UltimasCredenciales = credenciales;
        if (!Disponible)
            throw new ErrorDeFuenteNoDisponible("La fuente no responde.");
        return Task.FromResult(Estructura);
    }
}

internal sealed class PermisosFalsos : IVerificadorDePermisos
{
    public HashSet<string> UsuariosPermitidos { get; } = new();

    public Task<bool> TienePermiso(string usuario, string operacion, CancellationToken ct) =>
        Task.FromResult(UsuariosPermitidos.Contains(usuario));
}

internal sealed class PublicadorFalso : IPublicadorDeEventos
{
    public List<EventoDeDominio> Publicados { get; } = new();

    public Task Publicar(IEnumerable<EventoDeDominio> eventos, CancellationToken ct)
    {
        Publicados.AddRange(eventos);
        return Task.CompletedTask;
    }
}

// Un "contenedor" minimo hecho a mano para las pruebas (sin instalar paquetes).
internal sealed class ProveedorManual : IServiceProvider
{
    private readonly Dictionary<Type, object> _mapa = new();

    public void Registrar<T>(T instancia) where T : notnull => _mapa[typeof(T)] = instancia;

    public object? GetService(Type serviceType) => _mapa.TryGetValue(serviceType, out var instancia) ? instancia : null;
}

// Arma todo el sistema con dobles en memoria.
internal sealed class Escenario
{
    public RelojFijo Reloj { get; } = new();
    public UnidadDeTrabajoEnMemoria Uow { get; } = new();
    public LectorFalso Lector { get; } = new();
    public PermisosFalsos Permisos { get; } = new();
    public PublicadorFalso Publicador { get; } = new();
    public IMediador Puerto { get; }

    public Escenario()
    {
        Permisos.UsuariosPermitidos.Add("admin");

        var servicios = new ProveedorManual();
        servicios.Registrar<IManejadorDeComando<RegistrarFuenteComando, FuenteId>>(
            new RegistrarFuenteManejador(Uow, Publicador, Reloj));
        servicios.Registrar<IManejadorDeComando<AutorizarAccesoAMetadatosComando, Guid>>(
            new AutorizarAccesoAMetadatosManejador(Uow, Publicador, Reloj));
        servicios.Registrar<IManejadorDeComando<RevocarAutorizacionDeMetadatosComando, SinResultado>>(
            new RevocarAutorizacionDeMetadatosManejador(Uow, Publicador, Reloj));
        servicios.Registrar<IManejadorDeComando<ImportarMetadatosTecnicosComando, ResultadoDeImportacion>>(
            new ImportarMetadatosTecnicosManejador(Uow, new ILectorDeEstructura[] { Lector }, Publicador, Reloj));
        servicios.Registrar<IManejadorDeComando<CompararEstructurasComando, ResultadoDeComparacion>>(
            new CompararEstructurasManejador(Uow, Publicador, Reloj));
        servicios.Registrar<IManejadorDeConsulta<ObtenerFuenteConsulta, FuenteDto>>(new ObtenerFuenteManejador(Uow));
                servicios.Registrar<IManejadorDeComando<MarcarCambioRevisadoComando, SinResultado>>(
            new MarcarCambioRevisadoManejador(Uow, Reloj));
        servicios.Registrar<IManejadorDeComando<ReintentarImportacionComando, ResultadoDeImportacion>>(
            new ReintentarImportacionManejador(
                new ImportarMetadatosTecnicosManejador(Uow, new ILectorDeEstructura[] { Lector }, Publicador, Reloj)));
        servicios.Registrar<IManejadorDeComando<RegistrarAvisoDeCambioComando, Guid>>(
            new RegistrarAvisoDeCambioManejador(Uow, Reloj));
        servicios.Registrar<IManejadorDeConsulta<ObtenerEstadoDeImportacionConsulta, EstadoDeImportacionDto>>(
            new ObtenerEstadoDeImportacionManejador(Uow));
        servicios.Registrar<IManejadorDeConsulta<ListarAvisosConsulta, IReadOnlyList<AvisoDto>>>(
            new ListarAvisosManejador(Uow));
        servicios.Registrar<IManejadorDeConsulta<ListarCambiosConsulta, IReadOnlyList<CambioDto>>>(
            new ListarCambiosManejador(Uow));

        Puerto = new Mediador(servicios, new IPasoDeComando[] { new VerificacionDePermisosPaso(Permisos) });
    }

    public async Task<FuenteId> FuenteAutorizada()
    {
        var id = await Puerto.Enviar(new RegistrarFuenteComando("admin", "S1", TipoDeFuente.MotorRelacional, "usuario=ro;clave=123"));
        await Puerto.Enviar(new AutorizarAccesoAMetadatosComando("admin", id, "Jefa de administracion"));
        return id;
    }
}