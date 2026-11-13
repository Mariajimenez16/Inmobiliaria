using System.Reflection;
using System.Runtime.ExceptionServices;
using C1.Nucleo.Puertos.Entrada;

namespace C1.Nucleo.Aplicacion;

// IMPLEMENTA el puerto de entrada IMediador.
// SOLID:
//  - SRP: solo despacha. Los permisos (y cualquier otra preocupacion transversal) viven en los pasos del pipeline.
//  - OCP: un caso de uso nuevo es un manejador nuevo; un paso nuevo es un IPasoDeComando nuevo. Este archivo NO se modifica.
//  - DIP: depende de IServiceProvider (de .NET, no de un contenedor concreto) y de IPasoDeComando.
// Patrones: Mediator + Command + Pipeline (diapositivas 9b, 10 y 11).
// Limitacion declarada: resolver manejadores con IServiceProvider es un "service locator"; es lo habitual en un mediador.
public sealed class Mediador : IMediador
{
    private readonly IServiceProvider _servicios;
    private readonly IReadOnlyList<IPasoDeComando> _pasos;

    // El primer paso de la lista es el mas externo (se ejecuta primero).
    public Mediador(IServiceProvider servicios, IEnumerable<IPasoDeComando> pasos)
    {
        _servicios = servicios;
        _pasos = pasos.ToList();
    }

    public Task<TResultado> Enviar<TResultado>(IComando<TResultado> comando, CancellationToken ct = default)
    {
        // Se arma la cadena de adentro hacia afuera: el manejador al fondo y los pasos envolviendolo.
        Func<Task<TResultado>> siguiente = () =>
            Invocar<TResultado>(typeof(IManejadorDeComando<,>), comando.GetType(), comando, ct);

        for (var i = _pasos.Count - 1; i >= 0; i--)
        {
            var paso = _pasos[i];
            var posterior = siguiente;
            siguiente = () => paso.Ejecutar(comando, posterior, ct);
        }

        return siguiente();
    }

    public Task<TResultado> Consultar<TResultado>(IConsulta<TResultado> consulta, CancellationToken ct = default) =>
        Invocar<TResultado>(typeof(IManejadorDeConsulta<,>), consulta.GetType(), consulta, ct);

    private async Task<TResultado> Invocar<TResultado>(Type definicion, Type tipoMensaje, object mensaje, CancellationToken ct)
    {
        var tipoManejador = definicion.MakeGenericType(tipoMensaje, typeof(TResultado));
        var manejador = _servicios.GetService(tipoManejador)
            ?? throw new InvalidOperationException($"No hay un manejador registrado para '{tipoMensaje.Name}'.");

        var metodo = tipoManejador.GetMethod("Manejar")!;
        Task<TResultado> tarea;
        try
        {
            tarea = (Task<TResultado>)metodo.Invoke(manejador, new object[] { mensaje, ct })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return await tarea;
    }
}