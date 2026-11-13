using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

// Un paso del pipeline de comandos (diapositiva 9b). Recibe el comando y "siguiente", que representa el resto de la cadena
// (los pasos que faltan y, al final, el manejador). Un paso puede hacer algo antes, despues, o NO llamar a siguiente para cortar.
// SOLID - OCP: nuevos pasos (registro, validacion...) se agregan como clases, sin modificar el Mediador.
public interface IPasoDeComando
{
    Task<TResultado> Ejecutar<TResultado>(
        IComando<TResultado> comando, Func<Task<TResultado>> siguiente, CancellationToken ct);
}

// Control de acceso por rol (C7): sin permiso, se deniega ANTES de tocar el dominio (contrato 4.12).
// Solo actua sobre comandos que declaran IRequierePermiso; los demas pasan sin verificarse.
// SOLID - SRP: su unica razon de cambio es la politica de permisos. DIP: depende del puerto IVerificadorDePermisos.
public sealed class VerificacionDePermisosPaso : IPasoDeComando
{
    private readonly IVerificadorDePermisos _permisos;

    public VerificacionDePermisosPaso(IVerificadorDePermisos permisos) => _permisos = permisos;

    public async Task<TResultado> Ejecutar<TResultado>(
        IComando<TResultado> comando, Func<Task<TResultado>> siguiente, CancellationToken ct)
    {
        if (comando is IRequierePermiso protegido)
        {
            var usuario = Guarda.TextoObligatorio(protegido.Usuario, "usuario");
            if (!await _permisos.TienePermiso(usuario, protegido.Operacion, ct))
                throw new ErrorDePermisoDenegado(
                    $"El usuario '{usuario}' no tiene permiso para la operacion '{protegido.Operacion}'.");
        }

        return await siguiente();
    }
}