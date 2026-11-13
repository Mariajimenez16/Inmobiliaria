using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Cache;

// PATRON DECORATOR (igual que en C2): envuelve al repositorio real y le agrega cache SIN modificarlo.
// Solo se cachea Obtener(fuente, numero): una version de estructura es INMUTABLE una vez creada, asi que
// nunca queda desactualizada y no necesita invalidacion. Las demas lecturas (ultima, listar) cambian cuando se
// importa una version nueva, por eso siempre van al repositorio real.
// SOLID - OCP: se agrega comportamiento sin tocar RepositorioDeVersionesSqlite. LSP: se usa como cualquier IRepositorioDeVersiones.
public sealed class RepositorioDeVersionesConCache : IRepositorioDeVersiones
{
    private readonly IRepositorioDeVersiones _interno;
    private readonly ICacheDeLecturas _cache;

    public RepositorioDeVersionesConCache(IRepositorioDeVersiones interno, ICacheDeLecturas cache)
    {
        _interno = interno;
        _cache = cache;
    }

    public async Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct)
    {
        var clave = $"version:{fuenteId}:{numero}";
        var enCache = _cache.Obtener<VersionDeEstructura>(clave);
        if (enCache is not null)
            return enCache;

        var leida = await _interno.Obtener(fuenteId, numero, ct);
        if (leida is not null)
            _cache.Poner(clave, leida);
        return leida;
    }

    public Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct) =>
        _interno.ObtenerUltima(fuenteId, ct);

    public Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct) =>
        _interno.Listar(fuenteId, ct);

    public Task Agregar(VersionDeEstructura version, CancellationToken ct) =>
        _interno.Agregar(version, ct);
}