using C1.Adaptadores.Salida.Cifrado;
using C1.Adaptadores.Salida.Persistencia;
using C1.Nucleo.Dominio;
using Microsoft.Data.Sqlite;
using Xunit;

namespace C1.Pruebas;

public sealed class PersistenciaSqliteTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"c1-pruebas-{Guid.NewGuid():N}.db");
    private readonly OpcionesSqlite _opciones;
    private readonly ICifrador _cifrador = new CifradorAesGcm("clave-de-prueba");

    public PersistenciaSqliteTests()
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
    public async Task Una_fuente_con_su_autorizacion_se_guarda_y_se_recupera()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);

        using (var uow = Nueva())
        {
            await uow.Fuentes.Agregar(fuente, default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        var leida = await lectura.Fuentes.Obtener(fuente.Id, default);

        Assert.NotNull(leida);
        Assert.Equal("S1", leida!.Nombre);
        Assert.Equal(TipoDeFuente.MotorRelacional, leida.Tipo);
        Assert.Equal("Jefa de administracion", leida.AutorizacionVigente!.Autorizador);
        Assert.True(leida.SinCubrir);
        Assert.Empty(leida.Eventos); // reconstruir no levanta eventos
    }

    [Fact]
    public async Task Lo_que_no_se_confirma_no_se_guarda()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);

        using (var uow = Nueva())
        {
            await uow.Fuentes.Agregar(fuente, default);
            // no se llama a Confirmar: al liberar se hace rollback
        }

        using var lectura = Nueva();
        Assert.Null(await lectura.Fuentes.Obtener(fuente.Id, default));
    }

    [Fact]
    public async Task Actualizar_persiste_la_importacion_y_la_revocacion()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        using (var uow = Nueva())
        {
            await uow.Fuentes.Agregar(fuente, default);
            await uow.Confirmar(default);
        }

        using (var uow = Nueva())
        {
            var f = (await uow.Fuentes.Obtener(fuente.Id, default))!;
            f.AutorizarAcceso("Jefa de administracion", Ahora);
            f.RegistrarImportacion(Guid.NewGuid(), 1, 4, Ahora);
            await uow.Fuentes.Actualizar(f, default);
            await uow.Confirmar(default);
        }

        using (var uow = Nueva())
        {
            var f = (await uow.Fuentes.Obtener(fuente.Id, default))!;
            Assert.False(f.SinCubrir);
            f.RevocarAutorizacion(Ahora);
            await uow.Fuentes.Actualizar(f, default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        var final = (await lectura.Fuentes.Obtener(fuente.Id, default))!;
        Assert.Null(final.AutorizacionVigente);
        Assert.Single(final.Autorizaciones);
        Assert.NotNull(final.Autorizaciones[0].RevocadaEn);
    }

    [Fact]
    public async Task Las_credenciales_se_guardan_cifradas_y_se_recuperan_en_claro()
    {
        var id = FuenteId.Nueva();
        using (var uow = Nueva())
        {
            await uow.Credenciales.Guardar(id, "usuario=ro;clave=secreta", default);
            await uow.Confirmar(default);
        }

        using (var conexion = new SqliteConnection(_opciones.CadenaDeConexion))
        {
            conexion.Open();
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = "SELECT credenciales_cifradas FROM credenciales";
            var crudo = (string)cmd.ExecuteScalar()!;
            Assert.DoesNotContain("secreta", crudo);
            Assert.DoesNotContain("usuario", crudo);
        }

        using var lectura = Nueva();
        Assert.Equal("usuario=ro;clave=secreta", await lectura.Credenciales.Obtener(id, default));
    }

    [Fact]
    public async Task Las_versiones_se_guardan_con_sus_columnas_en_orden()
    {
        var f = FuenteId.Nueva();
        using (var uow = Nueva())
        {
            await uow.Versiones.Agregar(
                VersionDeEstructura.Crear(f, 1, Ahora, new[] { new MetadatoTecnico("t", "a", "int", null, true) }), default);
            await uow.Versiones.Agregar(
                VersionDeEstructura.Crear(f, 2, Ahora, new[]
                {
                    new MetadatoTecnico("t", "a", "varchar", 10, false),
                    new MetadatoTecnico("t", "b", "int", null, false)
                }), default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        var ultima = await lectura.Versiones.ObtenerUltima(f, default);
        Assert.Equal(2, ultima!.Numero);
        Assert.Equal(2, ultima.Columnas.Count);
        Assert.Equal("a", ultima.Columnas[0].Columna);
        Assert.Equal(10, ultima.Columnas[0].Longitud);

        var primera = await lectura.Versiones.Obtener(f, 1, default);
        Assert.Null(primera!.Columnas[0].Longitud);
        Assert.Equal(2, (await lectura.Versiones.Listar(f, default)).Count);
    }

    [Fact]
    public async Task Dos_versiones_con_el_mismo_numero_son_conflicto()
    {
        var f = FuenteId.Nueva();
        var columnas = new[] { new MetadatoTecnico("t", "a", "int", null, true) };
        using var uow = Nueva();
        await uow.Versiones.Agregar(VersionDeEstructura.Crear(f, 1, Ahora, columnas), default);

        await Assert.ThrowsAsync<ErrorDeConflicto>(() =>
            uow.Versiones.Agregar(VersionDeEstructura.Crear(f, 1, Ahora, columnas), default));
    }

    [Fact]
    public async Task Los_cambios_se_guardan_con_sus_diferencias()
    {
        var f = FuenteId.Nueva();
        var v1 = VersionDeEstructura.Crear(f, 1, Ahora, new[] { new MetadatoTecnico("t", "a", "int", null, true) });
        var v2 = VersionDeEstructura.Crear(f, 2, Ahora, new[]
        {
            new MetadatoTecnico("t", "a", "bigint", null, true),
            new MetadatoTecnico("t", "b", "int", null, false)
        });
        var cambio = CambioDeEstructura.Detectar(v1, v2, Ahora)!;

        using (var uow = Nueva())
        {
            await uow.Cambios.Agregar(cambio, default);
            await uow.Confirmar(default);
        }

        using var lectura = Nueva();
        var leidos = await lectura.Cambios.ListarPorFuente(f, default);
        var leido = Assert.Single(leidos);
        Assert.Equal(cambio.Id, leido.Id);
        Assert.Equal(2, leido.Diferencias.Count);
        Assert.Equal(1, leido.VersionAnterior);
        Assert.Equal(2, leido.VersionNueva);
    }
}