using C1.Nucleo.Aplicacion;
using C1.Nucleo.Puertos.Entrada;
using Xunit;

namespace C1.Pruebas;

public class PipelineTests
{
    private sealed record ComandoDePrueba(string Texto) : IComando<string>;

    private sealed class ManejadorDePrueba : IManejadorDeComando<ComandoDePrueba, string>
    {
        private readonly List<string> _traza;

        public ManejadorDePrueba(List<string> traza) => _traza = traza;

        public Task<string> Manejar(ComandoDePrueba comando, CancellationToken ct)
        {
            _traza.Add("manejador");
            return Task.FromResult(comando.Texto.ToUpperInvariant());
        }
    }

    private sealed class PasoQueAnota : IPasoDeComando
    {
        private readonly string _nombre;
        private readonly List<string> _traza;

        public PasoQueAnota(string nombre, List<string> traza)
        {
            _nombre = nombre;
            _traza = traza;
        }

        public async Task<TResultado> Ejecutar<TResultado>(
            IComando<TResultado> comando, Func<Task<TResultado>> siguiente, CancellationToken ct)
        {
            _traza.Add($"{_nombre}:antes");
            var resultado = await siguiente();
            _traza.Add($"{_nombre}:despues");
            return resultado;
        }
    }

    private sealed class PasoQueCorta : IPasoDeComando
    {
        public Task<TResultado> Ejecutar<TResultado>(
            IComando<TResultado> comando, Func<Task<TResultado>> siguiente, CancellationToken ct) =>
            throw new InvalidOperationException("cortado por el paso");
    }

    private static Mediador Armar(List<string> traza, params IPasoDeComando[] pasos)
    {
        var servicios = new ProveedorManual();
        servicios.Registrar<IManejadorDeComando<ComandoDePrueba, string>>(new ManejadorDePrueba(traza));
        return new Mediador(servicios, pasos);
    }

    [Fact]
    public async Task Los_pasos_envuelven_al_manejador_y_el_primero_es_el_mas_externo()
    {
        var traza = new List<string>();
        var mediador = Armar(traza, new PasoQueAnota("A", traza), new PasoQueAnota("B", traza));

        var resultado = await mediador.Enviar(new ComandoDePrueba("hola"));

        Assert.Equal("HOLA", resultado);
        Assert.Equal(new[] { "A:antes", "B:antes", "manejador", "B:despues", "A:despues" }, traza);
    }

    [Fact]
    public async Task Un_paso_que_corta_la_cadena_impide_que_se_llegue_al_manejador()
    {
        var traza = new List<string>();
        var mediador = Armar(traza, new PasoQueCorta());

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediador.Enviar(new ComandoDePrueba("hola")));

        Assert.Empty(traza);
    }
}