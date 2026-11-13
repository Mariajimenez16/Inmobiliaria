using C1.Nucleo.Dominio;
using Xunit;

namespace C1.Pruebas;

public class CambioDeEstructuraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly FuenteId Fuente1 = FuenteId.Nueva();

    private static VersionDeEstructura Version(FuenteId fuente, int numero, params MetadatoTecnico[] columnas) =>
        VersionDeEstructura.Crear(fuente, numero, Ahora, columnas);

    private static MetadatoTecnico Col(string tabla, string columna, string tipo, int? longitud = null, bool obligatorio = false) =>
        new(tabla, columna, tipo, longitud, obligatorio);

    [Fact]
    public void Sin_diferencias_no_hay_cambio_ni_evento()
    {
        var v1 = Version(Fuente1, 1, Col("clientes", "cedula", "varchar", 20));
        var v2 = Version(Fuente1, 2, Col("clientes", "cedula", "varchar", 20));

        Assert.Null(CambioDeEstructura.Detectar(v1, v2, Ahora));
    }

    [Fact]
    public void Detecta_columna_agregada_eliminada_y_modificada()
    {
        var v1 = Version(Fuente1, 1,
            Col("clientes", "cedula", "varchar", 20),
            Col("clientes", "obs3", "varchar", 50));
        var v2 = Version(Fuente1, 2,
            Col("clientes", "cedula", "varchar", 10),   // modificada: longitud 20 -> 10
            Col("clientes", "telefono", "varchar", 15)); // agregada; obs3 queda eliminada

        var cambio = CambioDeEstructura.Detectar(v1, v2, Ahora);

        Assert.NotNull(cambio);
        Assert.Equal(3, cambio!.Diferencias.Count);
        Assert.Contains(cambio.Diferencias, d => d.Tipo == TipoDeDiferencia.ColumnaAgregada && d.Columna == "telefono");
        Assert.Contains(cambio.Diferencias, d => d.Tipo == TipoDeDiferencia.ColumnaEliminada && d.Columna == "obs3");
        Assert.Contains(cambio.Diferencias, d => d.Tipo == TipoDeDiferencia.ColumnaModificada && d.Columna == "cedula");

        var evento = Assert.IsType<CambioDeEstructuraDetectado>(Assert.Single(cambio.Eventos));
        Assert.Equal(3, evento.CantidadDeDiferencias);
        Assert.Equal(1, evento.VersionAnterior);
        Assert.Equal(2, evento.VersionNueva);
    }

    [Fact]
    public void Comparar_versiones_de_fuentes_distintas_se_rechaza()
    {
        var v1 = Version(Fuente1, 1, Col("t", "c", "int"));
        var v2 = Version(FuenteId.Nueva(), 2, Col("t", "c", "int"));

        Assert.Throws<ErrorDeReglaDeNegocio>(() => CambioDeEstructura.Detectar(v1, v2, Ahora));
    }

    [Fact]
    public void La_version_nueva_debe_ser_posterior()
    {
        var v1 = Version(Fuente1, 2, Col("t", "c", "int"));
        var v2 = Version(Fuente1, 1, Col("t", "c", "int"));

        Assert.Throws<ErrorDeValidacion>(() => CambioDeEstructura.Detectar(v1, v2, Ahora));
    }

    [Fact]
    public void Una_version_sin_columnas_se_rechaza()
    {
        Assert.Throws<ErrorDeReglaDeNegocio>(() => VersionDeEstructura.Crear(Fuente1, 1, Ahora, Array.Empty<MetadatoTecnico>()));
    }

    [Fact]
    public void Una_version_con_columna_repetida_se_rechaza()
    {
        Assert.Throws<ErrorDeReglaDeNegocio>(() =>
            Version(Fuente1, 1, Col("clientes", "cedula", "varchar"), Col("CLIENTES", "Cedula", "varchar")));
    }
}