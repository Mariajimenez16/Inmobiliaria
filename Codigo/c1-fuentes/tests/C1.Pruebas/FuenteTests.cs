using C1.Nucleo.Dominio;
using Xunit;

namespace C1.Pruebas;

public class FuenteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static Fuente NuevaFuente()
    {
        var fuente = Fuente.Registrar("S1 Administracion de arrendamientos", TipoDeFuente.MotorRelacional, Ahora);
        fuente.ExtraerEventos();
        return fuente;
    }

    [Fact]
    public void Registrar_crea_la_fuente_y_levanta_el_evento()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);

        var evento = Assert.IsType<FuenteRegistrada>(Assert.Single(fuente.Eventos));
        Assert.Equal(fuente.Id, evento.FuenteId);
        Assert.True(fuente.SinCubrir);
    }

    [Fact]
    public void Registrar_sin_nombre_es_error_de_validacion()
    {
        Assert.Throws<ErrorDeValidacion>(() => Fuente.Registrar("   ", TipoDeFuente.MotorRelacional, Ahora));
    }

    [Fact]
    public void Autorizar_guarda_quien_autoriza_y_levanta_el_evento()
    {
        var fuente = NuevaFuente();

        fuente.AutorizarAcceso("Jefa de administracion", Ahora);

        Assert.Equal("Jefa de administracion", fuente.AutorizacionVigente!.Autorizador);
        var evento = Assert.IsType<AccesoAMetadatosAutorizado>(Assert.Single(fuente.Eventos));
        Assert.Equal("Jefa de administracion", evento.Autorizador);
    }

    [Fact]
    public void Autorizar_sin_autorizador_es_error_de_validacion()
    {
        Assert.Throws<ErrorDeValidacion>(() => NuevaFuente().AutorizarAcceso("", Ahora));
    }

    [Fact]
    public void Autorizar_dos_veces_es_conflicto()
    {
        var fuente = NuevaFuente();
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);

        Assert.Throws<ErrorDeConflicto>(() => fuente.AutorizarAcceso("Otra persona", Ahora));
    }

    [Fact]
    public void Revocar_sin_autorizacion_vigente_es_regla_de_negocio()
    {
        Assert.Throws<ErrorDeReglaDeNegocio>(() => NuevaFuente().RevocarAutorizacion(Ahora));
    }

    [Fact]
    public void Revocar_deja_la_fuente_sin_autorizacion_vigente_y_permite_autorizar_de_nuevo()
    {
        var fuente = NuevaFuente();
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);
        fuente.ExtraerEventos();

        fuente.RevocarAutorizacion(Ahora);

        Assert.Null(fuente.AutorizacionVigente);
        Assert.IsType<AutorizacionDeMetadatosRevocada>(Assert.Single(fuente.Eventos));
        fuente.AutorizarAcceso("Jefa de administracion", Ahora); // vuelve a ser posible
    }

    [Fact]
    public void Importar_sin_autorizacion_vigente_se_rechaza()
    {
        var fuente = NuevaFuente();

        Assert.Throws<ErrorDeReglaDeNegocio>(() => fuente.RegistrarImportacion(Guid.NewGuid(), 1, 10, Ahora));
        Assert.True(fuente.SinCubrir);
    }

    [Fact]
    public void Importar_con_autorizacion_levanta_el_evento_y_la_fuente_deja_de_estar_sin_cubrir()
    {
        var fuente = NuevaFuente();
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);
        fuente.ExtraerEventos();

        fuente.RegistrarImportacion(Guid.NewGuid(), 1, 10, Ahora);

        var evento = Assert.IsType<MetadatosTecnicosImportados>(Assert.Single(fuente.Eventos));
        Assert.Equal(10, evento.CantidadDeColumnas);
        Assert.False(fuente.SinCubrir);
    }

    [Fact]
    public void Importar_despues_de_revocar_se_rechaza()
    {
        var fuente = NuevaFuente();
        fuente.AutorizarAcceso("Jefa de administracion", Ahora);
        fuente.RevocarAutorizacion(Ahora);

        Assert.Throws<ErrorDeReglaDeNegocio>(() => fuente.RegistrarImportacion(Guid.NewGuid(), 1, 10, Ahora));
    }

    [Fact]
    public void ExtraerEventos_vacia_la_lista()
    {
        var fuente = Fuente.Registrar("S1", TipoDeFuente.MotorRelacional, Ahora);

        Assert.Single(fuente.ExtraerEventos());
        Assert.Empty(fuente.Eventos);
    }
}