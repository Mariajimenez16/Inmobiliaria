using C1.Nucleo.Dominio;

namespace C1.Nucleo.Puertos.Salida;

public interface IReloj
{
    DateTimeOffset Ahora { get; }
}

// Adaptadores A1 (motores relacionales) y A2 (CRM via proveedor) de la V4d. Hay uno por clase de fuente.
// Regla: lee SOLO la estructura (el catalogo del sistema), nunca registros con datos de personas.
// Si la fuente no responde, lanza ErrorDeFuenteNoDisponible.
public interface ILectorDeEstructura
{
    bool Atiende(TipoDeFuente tipo);
    Task<IReadOnlyList<MetadatoTecnico>> LeerEstructura(Fuente fuente, string? credenciales, CancellationToken ct);
}

// Consumo del servicio abierto de C7 (V8, relacion 1): verificarPermiso(usuario, operacion).
public interface IVerificadorDePermisos
{
    Task<bool> TienePermiso(string usuario, string operacion, CancellationToken ct);
}

// Entrega de los eventos al exterior (C2, C6). El adaptador traduce cada evento de dominio al contrato V1 (capa anticorrupcion).
public interface IPublicadorDeEventos
{
    Task Publicar(IEnumerable<EventoDeDominio> eventos, CancellationToken ct);
}