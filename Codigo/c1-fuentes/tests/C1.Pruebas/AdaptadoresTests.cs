using System.Security.Cryptography;
using System.Text.Json;
using C1.Adaptadores.Salida.Cifrado;
using C1.Adaptadores.Salida.Mensajeria;
using C1.Adaptadores.Salida.Simulacion;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using Xunit;

namespace C1.Pruebas;

public class CifradorTests
{
    [Fact]
    public void Cifrar_y_descifrar_devuelve_el_texto_original()
    {
        var cifrador = new CifradorAesGcm("clave-de-prueba");
        var cifrado = cifrador.Cifrar("usuario=ro;clave=secreta");

        Assert.DoesNotContain("secreta", cifrado);
        Assert.Equal("usuario=ro;clave=secreta", cifrador.Descifrar(cifrado));
    }

    [Fact]
    public void El_mismo_texto_cifrado_dos_veces_da_resultados_distintos()
    {
        var cifrador = new CifradorAesGcm("clave-de-prueba");

        Assert.NotEqual(cifrador.Cifrar("hola"), cifrador.Cifrar("hola"));
    }

    [Fact]
    public void Con_otra_clave_no_se_puede_descifrar()
    {
        var cifrado = new CifradorAesGcm("clave-uno").Cifrar("hola");

        Assert.ThrowsAny<CryptographicException>(() => new CifradorAesGcm("clave-dos").Descifrar(cifrado));
    }
}

public class MensajeriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class DestinoQueFalla : IDestinoDeMensajes
    {
        public string Nombre => "destino-roto";
        public bool Interesa(string tipoDeMensaje) => true;
        public Task Entregar(MensajePublicado mensaje, CancellationToken ct) => throw new InvalidOperationException("sin conexion");
    }

    private static PublicadorDeEventos Publicador(params IDestinoDeMensajes[] destinos) =>
        new(destinos, TraductoresDeEventos.Todos(), new RelojFijo());

    [Fact]
    public void El_traductor_convierte_el_evento_en_el_contrato_V1_con_nombres_snake_case()
    {
        var evento = new FuenteRegistrada(Guid.NewGuid(), Ahora, FuenteId.Nueva(), "S1", TipoDeFuente.MotorRelacional);
        var traductor = new FuenteRegistradaTraductor();

        Assert.True(traductor.Atiende(evento));
        var salida = traductor.Traducir(evento);

        Assert.Equal("FuenteRegistrada.v1", salida.Tipo);
        var json = JsonSerializer.Serialize(salida.Contrato, salida.Contrato.GetType());
        Assert.Contains("\"evento_id\"", json);
        Assert.Contains("\"fuente_id\"", json);
        Assert.Contains("\"nombre\"", json);
        Assert.Contains("\"ocurrio_en\"", json);
    }

    [Fact]
    public async Task Cada_destino_recibe_solo_los_mensajes_que_le_interesan()
    {
        var c2 = new DestinoEnMemoria("C2", new[] { "FuenteRegistrada.v1" });
        var c6 = new DestinoEnMemoria("C6", new[] { "MetadatosTecnicosImportados.v1", "CambioDeEstructuraDetectado.v1" });
        var publicador = Publicador(c2, c6);
        var id = FuenteId.Nueva();

        await publicador.Publicar(new EventoDeDominio[]
        {
            new FuenteRegistrada(Guid.NewGuid(), Ahora, id, "S1", TipoDeFuente.MotorRelacional),
            new AccesoAMetadatosAutorizado(Guid.NewGuid(), Ahora, id, Guid.NewGuid(), "Jefa"),
            new MetadatosTecnicosImportados(Guid.NewGuid(), Ahora, id, Guid.NewGuid(), 1, 4)
        }, default);

        Assert.Equal("FuenteRegistrada.v1", Assert.Single(c2.Recibidos).Tipo);
        Assert.Equal("MetadatosTecnicosImportados.v1", Assert.Single(c6.Recibidos).Tipo);
        Assert.Equal(3, publicador.MensajesPublicados.Count); // el de "acceso autorizado" no tiene consumidor, pero queda registrado
    }

    [Fact]
    public async Task Un_destino_caido_no_rompe_la_publicacion_y_queda_registrado()
    {
        var c2 = new DestinoEnMemoria("C2", new[] { "FuenteRegistrada.v1" });
        var publicador = Publicador(new DestinoQueFalla(), c2);

        await publicador.Publicar(new EventoDeDominio[]
        {
            new FuenteRegistrada(Guid.NewGuid(), Ahora, FuenteId.Nueva(), "S1", TipoDeFuente.MotorRelacional)
        }, default);

        Assert.Single(c2.Recibidos);
        Assert.Contains("destino-roto", Assert.Single(publicador.EntregasFallidas));
    }
}

public class SimulacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A1_exige_la_cuenta_de_solo_lectura()
    {
        var lector = new LectorA1MotorRelacional(new EstructurasSimuladasDeFuentes());
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);

        await Assert.ThrowsAsync<ErrorDeReglaDeNegocio>(() => lector.LeerEstructura(fuente, null, default));
        var estructura = await lector.LeerEstructura(fuente, "usuario=ro", default);
        Assert.NotEmpty(estructura);
    }

    [Fact]
    public async Task A2_no_esta_disponible_hasta_que_el_proveedor_entrega()
    {
        var entregas = new EstructurasSimuladasDeFuentes();
        var lector = new LectorA2Crm(entregas);
        var fuente = Fuente.Registrar("S2 CRM", TipoDeFuente.SistemaDeProveedor, Ahora);

        await Assert.ThrowsAsync<ErrorDeFuenteNoDisponible>(() => lector.LeerEstructura(fuente, null, default));

        entregas.Definir(fuente.Id, new[] { new MetadatoTecnico("interesados", "telefono", "varchar", 15, false) });
        var estructura = await lector.LeerEstructura(fuente, null, default);
        Assert.Single(estructura);
    }

    [Fact]
    public async Task C7_simulado_permite_al_administrador_y_deniega_a_consulta_dejando_registro()
    {
        var c7 = new VerificadorDePermisosSimuladoC7();

        Assert.True(await c7.TienePermiso("admin", RegistrarFuenteComando.Permiso, default));
        Assert.True(await c7.TienePermiso("ti", ImportarMetadatosTecnicosComando.Permiso, default));
        Assert.False(await c7.TienePermiso("consulta", RegistrarFuenteComando.Permiso, default));
        Assert.False(await c7.TienePermiso("desconocido", RegistrarFuenteComando.Permiso, default));

        Assert.Equal(2, c7.Denegaciones.Count);
    }
}