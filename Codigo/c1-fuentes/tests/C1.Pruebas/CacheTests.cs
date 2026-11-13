using C1.Adaptadores.Salida.Cache;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;
using Xunit;

namespace C1.Pruebas;

public sealed class CacheTests
{
    // Repositorio que cuenta cuantas veces lo consultan, para saber si la cache lo evito.
    private sealed class RepositorioContador : IRepositorioDeVersiones
    {
        private readonly RepositorioDeVersionesEnMemoria _real = new();
        public int LlamadasAObtener { get; private set; }
        public int LlamadasAObtenerUltima { get; private set; }

        public Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct)
        {
            LlamadasAObtener++;
            return _real.Obtener(fuenteId, numero, ct);
        }

        public Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct)
        {
            LlamadasAObtenerUltima++;
            return _real.ObtenerUltima(fuenteId, ct);
        }

        public Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct) =>
            _real.Listar(fuenteId, ct);

        public Task Agregar(VersionDeEstructura version, CancellationToken ct) => _real.Agregar(version, ct);
    }

    private static VersionDeEstructura UnaVersion(FuenteId fuente, DateTimeOffset ahora) =>
        VersionDeEstructura.Crear(fuente, 1, ahora, new[] { new MetadatoTecnico("clientes", "cedula", "varchar", 20, true) });

    private static (RepositorioContador Interno, CacheEnMemoria Cache, RepositorioDeVersionesConCache Decorado, RelojFijo Reloj, FuenteId Fuente)
        Armar()
    {
        var reloj = new RelojFijo();
        var interno = new RepositorioContador();
        var cache = new CacheEnMemoria(TimeSpan.FromSeconds(60), reloj);
        var fuente = FuenteId.Desde(Guid.NewGuid().ToString());
        return (interno, cache, new RepositorioDeVersionesConCache(interno, cache), reloj, fuente);
    }

    [Fact]
    public async Task La_segunda_lectura_de_una_version_sale_de_la_cache()
    {
        var (interno, cache, decorado, reloj, fuente) = Armar();
        await interno.Agregar(UnaVersion(fuente, reloj.Ahora), default);

        var primera = await decorado.Obtener(fuente, 1, default);
        var segunda = await decorado.Obtener(fuente, 1, default);

        Assert.NotNull(primera);
        Assert.Same(primera, segunda);
        Assert.Equal(1, interno.LlamadasAObtener);
        Assert.Equal(1, cache.Aciertos);
        Assert.Equal(1, cache.Fallos);
    }

    [Fact]
    public async Task Una_version_que_no_existe_no_se_guarda_en_cache()
    {
        var (interno, _, decorado, _, fuente) = Armar();

        Assert.Null(await decorado.Obtener(fuente, 7, default));
        Assert.Null(await decorado.Obtener(fuente, 7, default));

        Assert.Equal(2, interno.LlamadasAObtener);
    }

    [Fact]
    public async Task La_cache_expira_despues_de_su_tiempo_de_vida()
    {
        var (interno, _, decorado, reloj, fuente) = Armar();
        await interno.Agregar(UnaVersion(fuente, reloj.Ahora), default);

        await decorado.Obtener(fuente, 1, default);
        reloj.Ahora = reloj.Ahora.AddSeconds(61);
        await decorado.Obtener(fuente, 1, default);

        Assert.Equal(2, interno.LlamadasAObtener);
    }

    [Fact]
    public async Task La_ultima_version_nunca_se_cachea_porque_cambia_con_cada_importacion()
    {
        var (interno, _, decorado, reloj, fuente) = Armar();
        await interno.Agregar(UnaVersion(fuente, reloj.Ahora), default);

        await decorado.ObtenerUltima(fuente, default);
        await decorado.ObtenerUltima(fuente, default);

        Assert.Equal(2, interno.LlamadasAObtenerUltima);
    }
}