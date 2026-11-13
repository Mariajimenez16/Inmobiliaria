using C1.Nucleo.Dominio;

namespace C1.Nucleo.Puertos.Entrada;

// Resultado vacio para comandos que no devuelven nada.
public readonly record struct SinResultado;

// Registrar fuente -> evento Fuente registrada
// "Credenciales" es la cuenta de solo lectura de la fuente (V4f, A1). Se guarda cifrada; el dominio nunca la ve.
public sealed record RegistrarFuenteComando(string Usuario, string Nombre, TipoDeFuente Tipo, string? Credenciales = null)
    : IComando<FuenteId>, IRequierePermiso
{
    public const string Permiso = "fuentes.registrar";
    string IRequierePermiso.Operacion => Permiso;
}

// Autorizar acceso a metadatos -> evento Acceso a metadatos autorizado. Devuelve el id de la autorizacion.
public sealed record AutorizarAccesoAMetadatosComando(string Usuario, FuenteId FuenteId, string Autorizador)
    : IComando<Guid>, IRequierePermiso
{
    public const string Permiso = "fuentes.autorizar-acceso";
    string IRequierePermiso.Operacion => Permiso;
}

// Revocar autorizacion de metadatos -> evento Autorizacion de metadatos revocada
public sealed record RevocarAutorizacionDeMetadatosComando(string Usuario, FuenteId FuenteId)
    : IComando<SinResultado>, IRequierePermiso
{
    public const string Permiso = "fuentes.revocar-autorizacion";
    string IRequierePermiso.Operacion => Permiso;
}

// Importar metadatos tecnicos -> evento Metadatos tecnicos importados
public sealed record ImportarMetadatosTecnicosComando(string Usuario, FuenteId FuenteId)
    : IComando<ResultadoDeImportacion>, IRequierePermiso
{
    public const string Permiso = "metadatos.importar";
    string IRequierePermiso.Operacion => Permiso;
}

// Comparar estructuras -> evento Cambio de estructura detectado (SOLO si hay diferencias).
// Si no se indican versiones, se comparan las dos mas recientes de la fuente.
public sealed record CompararEstructurasComando(string Usuario, FuenteId FuenteId, int? VersionAnterior = null, int? VersionNueva = null)
    : IComando<ResultadoDeComparacion>, IRequierePermiso
{
    public const string Permiso = "estructuras.comparar";
    string IRequierePermiso.Operacion => Permiso;
}

// Marcar cambio como revisado (contrato 4.9). El revisor es el usuario que ejecuta el comando.
public sealed record MarcarCambioRevisadoComando(string Usuario, FuenteId FuenteId, Guid CambioId)
    : IComando<SinResultado>, IRequierePermiso
{
    public const string Permiso = "cambios.marcar-revisado";
    string IRequierePermiso.Operacion => Permiso;
}

// Reintentar importacion (contrato 3.2): vuelve a importar con las mismas reglas que Importar.
public sealed record ReintentarImportacionComando(string Usuario, FuenteId FuenteId)
    : IComando<ResultadoDeImportacion>, IRequierePermiso
{
    public const string Permiso = "metadatos.reintentar-importacion";
    string IRequierePermiso.Operacion => Permiso;
}

// Adaptador A3: el aviso llega desde el proveedor, no desde una persona del sistema, por eso NO pide permiso de usuario.
// Si FuenteId es null, el aviso queda pendiente de clasificar.
public sealed record RegistrarAvisoDeCambioComando(string Proveedor, string Descripcion, FuenteId? FuenteId)
    : IComando<Guid>;