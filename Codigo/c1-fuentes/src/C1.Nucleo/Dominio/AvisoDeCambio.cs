namespace C1.Nucleo.Dominio;

public enum EstadoDeAviso
{
    PendienteDeClasificar,
    Asociado
}

// Adaptador A3 (V8, V4f): el proveedor avisa que cambio algo en su sistema.
// Si el aviso no identifica la fuente, queda PENDIENTE DE CLASIFICAR; si la identifica, queda asociado a ella.
// El aviso NO importa nada por si solo: solo deja constancia (y hace que la fuente se vea "desactualizada").
// SOLID - SRP: solo conoce sus propios datos y su regla de estado.
public sealed class AvisoDeCambio
{
    private AvisoDeCambio(Guid id, string proveedor, string descripcion, FuenteId? fuenteId, DateTimeOffset recibidoEn)
    {
        Id = id;
        Proveedor = proveedor;
        Descripcion = descripcion;
        FuenteId = fuenteId;
        RecibidoEn = recibidoEn;
    }

    public Guid Id { get; }
    public string Proveedor { get; }
    public string Descripcion { get; }
    public FuenteId? FuenteId { get; }
    public DateTimeOffset RecibidoEn { get; }
    public EstadoDeAviso Estado => FuenteId is null ? EstadoDeAviso.PendienteDeClasificar : EstadoDeAviso.Asociado;

    public static AvisoDeCambio Registrar(string proveedor, string descripcion, FuenteId? fuenteId, DateTimeOffset ahora) =>
        new(Guid.NewGuid(),
            Guarda.TextoObligatorio(proveedor, "proveedor"),
            Guarda.TextoObligatorio(descripcion, "descripcion", 1000),
            fuenteId,
            ahora);

    public static AvisoDeCambio Reconstituir(
        Guid id, string proveedor, string descripcion, FuenteId? fuenteId, DateTimeOffset recibidoEn) =>
        new(id, proveedor, descripcion, fuenteId, recibidoEn);
}