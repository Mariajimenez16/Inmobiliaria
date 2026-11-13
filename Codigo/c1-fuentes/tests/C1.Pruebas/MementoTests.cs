using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;
using Xunit;

namespace C1.Pruebas;

public sealed class MementoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    // Repositorio de versiones que falla al guardar, para simular un conflicto al persistir.
    private sealed class VersionesQueFallan : IRepositorioDeVersiones
    {
        public Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct) =>
            Task.FromResult<VersionDeEstructura?>(null);

        public Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct) =>
            Task.FromResult<VersionDeEstructura?>(null);

        public Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VersionDeEstructura>>(Array.Empty<VersionDeEstructura>());

        public Task Agregar(VersionDeEstructura version, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado al guardar la version.");
    }

    [Fact]
    public void Restaurar_devuelve_la_fuente_al_estado_de_la_foto()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        var memento = fuente.CrearMemento();

        fuente.AutorizarAcceso("Jefa de administracion", Ahora);
        fuente.RegistrarImportacion(Guid.NewGuid(), 1, 3, Ahora);
        fuente.RevocarAutorizacion(Ahora);

        fuente.Restaurar(memento);

        Assert.Empty(fuente.Autorizaciones);
        Assert.Null(fuente.AutorizacionVigente);
        Assert.Null(fuente.UltimaImportacionEn);
        Assert.True(fuente.SinCubrir);
        Assert.Single(fuente.Eventos); // solo queda "Fuente registrada"; los demas eventos no ocurrieron
    }

    [Fact]
    public void La_foto_no_cambia_aunque_la_fuente_se_modifique_despues()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);
        var memento = fuente.CrearMemento();

        fuente.RevocarAutorizacion(Ahora.AddHours(1));

        Assert.Null(memento.Autorizaciones.Single().RevocadaEn);
        fuente.Restaurar(memento);
        Assert.NotNull(fuente.AutorizacionVigente);
    }

    [Fact]
    public void Un_memento_de_otra_fuente_se_rechaza()
    {
        var una = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);
        var otra = Fuente.Registrar("S2", TipoDeFuente.MotorRelacional, Ahora);

        Assert.Throws<ErrorDeReglaDeNegocio>(() => una.Restaurar(otra.CrearMemento()));
    }

    [Fact]
    public async Task Si_falla_al_guardar_la_importacion_la_fuente_vuelve_a_su_estado_anterior()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        e.Uow.VersionesSustituto = new VersionesQueFallan();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id)));

        var fuente = e.Uow.FuentesMem.Datos[id];
        Assert.Null(fuente.UltimaImportacionEn);
        Assert.True(fuente.SinCubrir);
        Assert.Empty(fuente.Eventos);                // ningun evento "Metadatos importados" quedo pendiente
        Assert.NotNull(fuente.AutorizacionVigente);  // la autorizacion previa se conserva
        Assert.Empty(e.Publicador.Publicados.OfType<MetadatosTecnicosImportados>());
    }
}