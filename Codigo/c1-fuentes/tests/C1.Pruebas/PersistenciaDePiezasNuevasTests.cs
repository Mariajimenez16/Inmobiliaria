using C1.Adaptadores.Salida.Cifrado;
using C1.Adaptadores.Salida.Persistencia;
using C1.Nucleo.Dominio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace C1.Pruebas;

public sealed class PersistenciaDePiezasNuevasTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"c1-pruebas-{Guid.NewGuid():N}.db");
    private readonly OpcionesSqlite _opciones;
    private readonly ICifrador _cifrador = new CifradorAesGcm("clave-de-prueba");

    public PersistenciaDePiezasNuevasTests()
    {
        _opciones = new OpcionesSqlite(_ruta);
        EsquemaSqlite.Inicializar(_opciones);
    }

    private UnidadDeTrabajoSqlite Nueva() => new(_opciones, _cifrador);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_ruta); } catch (IOException) { }
    }

    [Fact]
    public async Task La_revision_de_un_cambio_se_guarda_y_se_recupera()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        var v1 = VersionDeEstructura.Crear(fuente.Id, 1, Ahora, new[]
        {
            new MetadatoTecnico("clientes", "cedula", "varchar", 20, true)
        });
        var v2 = VersionDeEstructura.Crear(fuente.Id, 2, Ahora, new[]
        {
            new MetadatoTecnico("clientes", "cedula", "varchar", 20, true),
            new MetadatoTecnico("clientes", "correo", "varchar", 100, false)
        });
        var cambio = CambioDeEstructura.Detectar(v1, v2, Ahora)!;

        using (var uow = Nueva())
        {
            await uow.Cambios.Agregar(cambio, default);
            await uow.Confirmar(default);
        }

        using (var uow = Nueva())
        {
            var leido = await uow.Cambios.Obtener(cambio.Id, default);
            Assert.False(leido!.Revisado);
            leido.MarcarRevisado("admin", Ahora.AddHours(1));
            await uow.Cambios.Actualizar(leido, default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        var cambios = await lectura.Cambios.ListarPorFuente(fuente.Id, default);
        var revisado = Assert.Single(cambios);
        Assert.True(revisado.Revisado);
        Assert.Equal("admin", revisado.RevisadoPor);
        Assert.Equal(Ahora.AddHours(1), revisado.RevisadoEn);
        Assert.NotEmpty(revisado.Diferencias);
    }

    [Fact]
    public async Task Los_avisos_se_guardan_y_se_filtran_por_fuente()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.SistemaDeProveedor, Ahora);
        var conFuente = AvisoDeCambio.Registrar("Proveedor CRM", "Cambio en clientes", fuente.Id, Ahora);
        var sinFuente = AvisoDeCambio.Registrar("Proveedor CRM", "Aviso general", null, Ahora.AddMinutes(1));

        using (var uow = Nueva())
        {
            await uow.Avisos.Agregar(conFuente, default);
            await uow.Avisos.Agregar(sinFuente, default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        Assert.Equal(2, (await lectura.Avisos.Listar(null, default)).Count);

        var deLaFuente = Assert.Single(await lectura.Avisos.Listar(fuente.Id, default));
        Assert.Equal(EstadoDeAviso.Asociado, deLaFuente.Estado);

        var pendiente = (await lectura.Avisos.Listar(null, default)).Single(a => a.Id == sinFuente.Id);
        Assert.Equal(EstadoDeAviso.PendienteDeClasificar, pendiente.Estado);
    }
}