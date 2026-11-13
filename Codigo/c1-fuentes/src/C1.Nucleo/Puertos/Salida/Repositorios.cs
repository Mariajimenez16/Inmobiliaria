using C1.Nucleo.Dominio;

namespace C1.Nucleo.Puertos.Salida;

// PUERTOS DE SALIDA: el nucleo define lo que NECESITA; los adaptadores (SQLite, etc.) lo implementan. (DIP)
// SOLID - ISP: interfaces pequenas, una por tipo de dato, en lugar de un "repositorio gigante".

public interface IRepositorioDeFuentes
{
    Task<Fuente?> Obtener(FuenteId id, CancellationToken ct);
    Task<IReadOnlyList<Fuente>> Listar(CancellationToken ct);
    Task Agregar(Fuente fuente, CancellationToken ct);
    Task Actualizar(Fuente fuente, CancellationToken ct);
}

public interface IRepositorioDeVersiones
{
    Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct);
    Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct);
    Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct);
    Task Agregar(VersionDeEstructura version, CancellationToken ct);
}

public interface IRepositorioDeCambios
{
    Task Agregar(CambioDeEstructura cambio, CancellationToken ct);
    Task<CambioDeEstructura?> Obtener(Guid cambioId, CancellationToken ct);
    Task Actualizar(CambioDeEstructura cambio, CancellationToken ct);
    Task<IReadOnlyList<CambioDeEstructura>> ListarPorFuente(FuenteId fuenteId, CancellationToken ct);
}

// Avisos de los proveedores (adaptador A3). ISP: interfaz propia, no se mezcla con las fuentes.
public interface IRepositorioDeAvisos
{
    Task Agregar(AvisoDeCambio aviso, CancellationToken ct);
    Task<IReadOnlyList<AvisoDeCambio>> Listar(FuenteId? fuenteId, CancellationToken ct);
}

// Las credenciales de la cuenta de solo lectura NO viven en el dominio. El adaptador las guarda CIFRADAS.
public interface IAlmacenDeCredenciales
{
    Task Guardar(FuenteId fuenteId, string credenciales, CancellationToken ct);
    Task<string?> Obtener(FuenteId fuenteId, CancellationToken ct);
}

// Agrupa los repositorios para que una operacion que toca varios (importar = version + fuente) se confirme junta o no se confirme.
public interface IUnidadDeTrabajo
{
    IRepositorioDeFuentes Fuentes { get; }
    IRepositorioDeVersiones Versiones { get; }
    IRepositorioDeCambios Cambios { get; }
    IRepositorioDeAvisos Avisos { get; }
    IAlmacenDeCredenciales Credenciales { get; }
    Task Confirmar(CancellationToken ct);
}