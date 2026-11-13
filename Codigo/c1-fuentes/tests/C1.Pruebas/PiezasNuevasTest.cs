using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using Xunit;

namespace C1.Pruebas;

public sealed class PiezasNuevasTests
{
    private static MetadatoTecnico[] EstructuraConCambio() => new[]
    {
        new MetadatoTecnico("clientes", "cedula", "varchar", 20, true),
        new MetadatoTecnico("clientes", "correo", "varchar", 100, false)
    };

    // Deja registrado un cambio de estructura: importa, cambia lo que entrega la fuente, importa de nuevo y compara.
    private static async Task<(FuenteId Id, Guid CambioId)> FuenteConCambio(Escenario e)
    {
        var id = await e.FuenteAutorizada();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        e.Lector.Estructura = EstructuraConCambio();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        var comparacion = await e.Puerto.Enviar(new CompararEstructurasComando("admin", id));
        return (id, comparacion.CambioId!.Value);
    }

    // ---------- 4.9 marcarRevisado

    [Fact]
    public async Task Marcar_revisado_guarda_quien_y_cuando()
    {
        var e = new Escenario();
        var (id, cambioId) = await FuenteConCambio(e);
        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(2);

        await e.Puerto.Enviar(new MarcarCambioRevisadoComando("admin", id, cambioId));

        var cambio = (await e.Puerto.Consultar(new ListarCambiosConsulta(id))).Single();
        Assert.True(cambio.Revisado);
        Assert.Equal("admin", cambio.RevisadoPor);
        Assert.Equal(e.Reloj.Ahora, cambio.RevisadoEn);
    }

    [Fact]
    public async Task Marcar_revisado_dos_veces_es_un_conflicto()
    {
        var e = new Escenario();
        var (id, cambioId) = await FuenteConCambio(e);
        await e.Puerto.Enviar(new MarcarCambioRevisadoComando("admin", id, cambioId));

        await Assert.ThrowsAsync<ErrorDeConflicto>(() =>
            e.Puerto.Enviar(new MarcarCambioRevisadoComando("admin", id, cambioId)));
    }

    [Fact]
    public async Task Marcar_revisado_un_cambio_que_no_existe_es_no_encontrado()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            e.Puerto.Enviar(new MarcarCambioRevisadoComando("admin", id, Guid.NewGuid())));
    }

    [Fact]
    public async Task Marcar_revisado_sin_permiso_se_deniega()
    {
        var e = new Escenario();
        var (id, cambioId) = await FuenteConCambio(e);

        await Assert.ThrowsAsync<ErrorDePermisoDenegado>(() =>
            e.Puerto.Enviar(new MarcarCambioRevisadoComando("consulta", id, cambioId)));
    }

    // ---------- 3.2 reintentarImportacion

    [Fact]
    public async Task Reintentar_la_importacion_despues_de_un_fallo_crea_la_version()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        e.Lector.Disponible = false;
        await Assert.ThrowsAsync<ErrorDeFuenteNoDisponible>(() =>
            e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id)));

        e.Lector.Disponible = true;
        var resultado = await e.Puerto.Enviar(new ReintentarImportacionComando("admin", id));

        Assert.Equal(1, resultado.NumeroDeVersion);
        Assert.Single(e.Uow.VersionesMem.Datos);
    }

    [Fact]
    public async Task Reintentar_sin_autorizacion_vigente_se_rechaza()
    {
        var e = new Escenario();
        var id = await e.Puerto.Enviar(new RegistrarFuenteComando("admin", "S1", TipoDeFuente.MotorRelacional, "u=ro"));

        await Assert.ThrowsAsync<ErrorDeReglaDeNegocio>(() =>
            e.Puerto.Enviar(new ReintentarImportacionComando("admin", id)));
        Assert.Equal(0, e.Lector.Lecturas);
    }

    // ---------- 3.2 consultarEstadoImportacion + A3

    [Fact]
    public async Task El_estado_pasa_de_SinImportar_a_Disponible_a_Desactualizada_y_vuelve_a_Disponible()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();

        Assert.Equal("SinImportar", (await e.Puerto.Consultar(new ObtenerEstadoDeImportacionConsulta(id))).Estado);

        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        var disponible = await e.Puerto.Consultar(new ObtenerEstadoDeImportacionConsulta(id));
        Assert.Equal("Disponible", disponible.Estado);
        Assert.Equal(1, disponible.UltimaVersion);

        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(1);
        await e.Puerto.Enviar(new RegistrarAvisoDeCambioComando("Proveedor CRM", "Cambiaron los campos del cliente", id));
        var desactualizada = await e.Puerto.Consultar(new ObtenerEstadoDeImportacionConsulta(id));
        Assert.Equal("Desactualizada", desactualizada.Estado);
        Assert.Equal(1, desactualizada.AvisosPendientes);

        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(1);
        await e.Puerto.Enviar(new ReintentarImportacionComando("admin", id));
        Assert.Equal("Disponible", (await e.Puerto.Consultar(new ObtenerEstadoDeImportacionConsulta(id))).Estado);
    }

    [Fact]
    public async Task Un_aviso_sin_fuente_queda_pendiente_de_clasificar()
    {
        var e = new Escenario();

        await e.Puerto.Enviar(new RegistrarAvisoDeCambioComando("Proveedor CRM", "Nueva version del modulo", null));

        var aviso = (await e.Puerto.Consultar(new ListarAvisosConsulta())).Single();
        Assert.Equal("PendienteDeClasificar", aviso.Estado);
        Assert.Null(aviso.FuenteId);
    }

    [Fact]
    public async Task Un_aviso_con_una_fuente_que_no_existe_es_no_encontrado()
    {
        var e = new Escenario();
        var inexistente = FuenteId.Desde(Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() =>
            e.Puerto.Enviar(new RegistrarAvisoDeCambioComando("Proveedor CRM", "Aviso", inexistente)));
    }
}