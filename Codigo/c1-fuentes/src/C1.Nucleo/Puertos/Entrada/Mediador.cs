namespace C1.Nucleo.Puertos.Entrada;

// Un comando es una SOLICITUD de cambio. Un TResultado es lo que devuelve si tiene exito.
public interface IComando<TResultado> { }

// Una consulta solo LEE; no cambia nada ni produce eventos.
public interface IConsulta<TResultado> { }

// Los comandos que cambian algo exigen que el usuario tenga permiso en C7 (control de acceso por rol).
// OJO: el "Usuario" (quien opera la herramienta) NO es el "Autorizador" de una fuente (V6.a: son cosas distintas).
public interface IRequierePermiso
{
    string Usuario { get; }
    string Operacion { get; }
}

// PUERTO DE ENTRADA PRINCIPAL: los adaptadores que conducen al nucleo (REST, consola) solo conocen esto.
public interface IMediador
{
    Task<TResultado> Enviar<TResultado>(IComando<TResultado> comando, CancellationToken ct = default);
    Task<TResultado> Consultar<TResultado>(IConsulta<TResultado> consulta, CancellationToken ct = default);
}

// Un caso de uso = un manejador de UN comando (SRP).
public interface IManejadorDeComando<in TComando, TResultado> where TComando : IComando<TResultado>
{
    Task<TResultado> Manejar(TComando comando, CancellationToken ct);
}

public interface IManejadorDeConsulta<in TConsulta, TResultado> where TConsulta : IConsulta<TResultado>
{
    Task<TResultado> Manejar(TConsulta consulta, CancellationToken ct);
}