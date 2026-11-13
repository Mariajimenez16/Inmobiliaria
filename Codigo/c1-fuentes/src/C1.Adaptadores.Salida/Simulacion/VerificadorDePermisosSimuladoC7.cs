using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Simulacion;

// SIMULA el servicio abierto de C7 (V8, relacion 1): verificarPermiso(usuario, operacion).
// OJO: la matriz real de permisos esta PENDIENTE de aprobacion en el diseno (V6.h). Esta es solo una regla minima
// tomada de la V4c: administran las fuentes el Administrador del diccionario y el Coordinador de TI.
// Los demas roles no operan sobre fuentes. "Sin permiso: se deniega y se registra" (contrato 4.12).
public sealed class VerificadorDePermisosSimuladoC7 : IVerificadorDePermisos
{
    private static readonly string[] OperacionesDeC1 =
    {
        RegistrarFuenteComando.Permiso,
        AutorizarAccesoAMetadatosComando.Permiso,
        RevocarAutorizacionDeMetadatosComando.Permiso,
        ImportarMetadatosTecnicosComando.Permiso,
        CompararEstructurasComando.Permiso,
        CompararEstructurasComando.Permiso,
        MarcarCambioRevisadoComando.Permiso,
        ReintentarImportacionComando.Permiso
    };

    private readonly Dictionary<string, string> _rolDeUsuario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "Administrador",
        ["ti"] = "TI",
        ["consulta"] = "Consulta",
        ["oficial"] = "Oficial"
    };

    private readonly Dictionary<string, HashSet<string>> _permisosDeRol = new()
    {
        ["Administrador"] = new HashSet<string>(OperacionesDeC1),
        ["TI"] = new HashSet<string>(OperacionesDeC1),
        ["Consulta"] = new HashSet<string>(),
        ["Oficial"] = new HashSet<string>()
    };

    private readonly List<string> _denegaciones = new();
    private readonly object _candado = new();

    // Bitacora de denegaciones (contrato 4.12). Se consulta desde la API de simulacion.
    public IReadOnlyList<string> Denegaciones
    {
        get
        {
            lock (_candado) return _denegaciones.ToList();
        }
    }

    public Task<bool> TienePermiso(string usuario, string operacion, CancellationToken ct)
    {
        var permitido = _rolDeUsuario.TryGetValue(usuario, out var rol)
                        && _permisosDeRol.TryGetValue(rol, out var permisos)
                        && permisos.Contains(operacion);

        if (!permitido)
        {
            lock (_candado) _denegaciones.Add($"Denegado: usuario '{usuario}' / operacion '{operacion}'");
        }

        return Task.FromResult(permitido);
    }
}