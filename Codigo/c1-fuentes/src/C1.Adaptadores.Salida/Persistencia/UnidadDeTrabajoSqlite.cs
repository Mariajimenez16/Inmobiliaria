using C1.Adaptadores.Salida.Cache;
using C1.Adaptadores.Salida.Cifrado;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IUnidadDeTrabajo. Se crea UNA por solicitud (el Host la registra como "scoped").
// Si el caso de uso falla antes de Confirmar, al liberarla se hace rollback automaticamente.
// La cache es opcional: sin ella (null) el repositorio de versiones funciona igual, solo que sin cache.
public sealed class UnidadDeTrabajoSqlite : IUnidadDeTrabajo, IDisposable
{
    private readonly ContextoSqlite _ctx;

    public UnidadDeTrabajoSqlite(OpcionesSqlite opciones, ICifrador cifrador, ICacheDeLecturas? cache = null)
    {
        _ctx = new ContextoSqlite(opciones.CadenaDeConexion);
        Fuentes = new RepositorioDeFuentesSqlite(_ctx);

        IRepositorioDeVersiones versiones = new RepositorioDeVersionesSqlite(_ctx);
        Versiones = cache is null ? versiones : new RepositorioDeVersionesConCache(versiones, cache);

        Cambios = new RepositorioDeCambiosSqlite(_ctx);
        Avisos = new RepositorioDeAvisosSqlite(_ctx);
        Credenciales = new AlmacenDeCredencialesSqlite(_ctx, cifrador);
    }

    public IRepositorioDeFuentes Fuentes { get; }
    public IRepositorioDeVersiones Versiones { get; }
    public IRepositorioDeCambios Cambios { get; }
    public IRepositorioDeAvisos Avisos { get; }
    public IAlmacenDeCredenciales Credenciales { get; }

    public Task Confirmar(CancellationToken ct) => _ctx.Confirmar(ct);

    public void Dispose() => _ctx.Dispose();
}