using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using Xunit;

namespace C1.Pruebas;

public class CasosDeUsoTests
{
    [Fact]
    public async Task Registrar_fuente_guarda_las_credenciales_y_publica_el_evento()
    {
        var e = new Escenario();

        var id = await e.Puerto.Enviar(new RegistrarFuenteComando("admin", "S1", TipoDeFuente.MotorRelacional, "usuario=ro;clave=123"));

        Assert.True(e.Uow.FuentesMem.Datos.ContainsKey(id));
        Assert.Equal("usuario=ro;clave=123", e.Uow.CredencialesMem.Datos[id]);
        Assert.IsType<FuenteRegistrada>(Assert.Single(e.Publicador.Publicados));
        Assert.Equal(1, e.Uow.Confirmaciones);
    }

    [Fact]
    public async Task Un_usuario_sin_permiso_es_rechazado_y_no_se_guarda_nada()
    {
        var e = new Escenario();

        await Assert.ThrowsAsync<ErrorDePermisoDenegado>(() =>
            e.Puerto.Enviar(new RegistrarFuenteComando("intruso", "S1", TipoDeFuente.MotorRelacional)));

        Assert.Empty(e.Uow.FuentesMem.Datos);
        Assert.Empty(e.Publicador.Publicados);
    }

    [Fact]
    public async Task Importar_sin_autorizacion_se_rechaza_sin_tocar_la_fuente_externa()
    {
        var e = new Escenario();
        var id = await e.Puerto.Enviar(new RegistrarFuenteComando("admin", "S1", TipoDeFuente.MotorRelacional));

        await Assert.ThrowsAsync<ErrorDeReglaDeNegocio>(() => e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id)));

        Assert.Equal(0, e.Lector.Lecturas);
    }

    [Fact]
    public async Task Importar_con_autorizacion_crea_la_version_1_usa_las_credenciales_y_publica_el_evento()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();

        var resultado = await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));

        Assert.Equal(1, resultado.NumeroDeVersion);
        Assert.Equal(2, resultado.CantidadDeColumnas);
        Assert.Equal("usuario=ro;clave=123", e.Lector.UltimasCredenciales);
        Assert.Single(e.Publicador.Publicados.OfType<MetadatosTecnicosImportados>());
        Assert.False(e.Uow.FuentesMem.Datos[id].SinCubrir);
    }

    [Fact]
    public async Task Una_fuente_sin_diccionario_tecnico_no_se_puede_importar()
    {
        var e = new Escenario();
        var id = await e.Puerto.Enviar(new RegistrarFuenteComando("admin", "Hojas de calculo", TipoDeFuente.NoEstructurada));
        await e.Puerto.Enviar(new AutorizarAccesoAMetadatosComando("admin", id, "Jefa de administracion"));

        await Assert.ThrowsAsync<ErrorDeReglaDeNegocio>(() => e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id)));
    }

    [Fact]
    public async Task Si_la_fuente_no_responde_se_informa_la_ultima_version_conocida()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        e.Lector.Disponible = false;

        var error = await Assert.ThrowsAsync<ErrorDeFuenteNoDisponible>(() =>
            e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id)));

        Assert.Contains("v1", error.Detalle);
        Assert.Single(e.Uow.VersionesMem.Datos);
    }

    [Fact]
    public async Task Comparar_con_una_sola_version_se_rechaza()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));

        await Assert.ThrowsAsync<ErrorDeReglaDeNegocio>(() => e.Puerto.Enviar(new CompararEstructurasComando("admin", id)));
    }

    [Fact]
    public async Task Dos_importaciones_iguales_no_generan_cambio_ni_evento()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));

        var resultado = await e.Puerto.Enviar(new CompararEstructurasComando("admin", id));

        Assert.False(resultado.HayCambio);
        Assert.Empty(e.Uow.CambiosMem.Datos);
        Assert.Empty(e.Publicador.Publicados.OfType<CambioDeEstructuraDetectado>());
    }

    [Fact]
    public async Task Un_cambio_de_estructura_se_registra_y_se_publica()
    {
        var e = new Escenario();
        var id = await e.FuenteAutorizada();
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));
        e.Lector.Estructura = new[]
        {
            new MetadatoTecnico("clientes", "cedula", "varchar", 10, true),   // longitud 20 -> 10
            new MetadatoTecnico("clientes", "telefono", "varchar", 15, false) // nombre eliminada, telefono agregada
        };
        await e.Puerto.Enviar(new ImportarMetadatosTecnicosComando("admin", id));

        var resultado = await e.Puerto.Enviar(new CompararEstructurasComando("admin", id));

        Assert.True(resultado.HayCambio);
        Assert.Equal(3, resultado.Diferencias.Count);
        Assert.Single(e.Uow.CambiosMem.Datos);
        var evento = Assert.Single(e.Publicador.Publicados.OfType<CambioDeEstructuraDetectado>());
        Assert.Equal(3, evento.CantidadDeDiferencias);
    }

    [Fact]
    public async Task Consultar_una_fuente_inexistente_es_no_encontrado()
    {
        var e = new Escenario();

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => e.Puerto.Consultar(new ObtenerFuenteConsulta(FuenteId.Nueva())));
    }
}