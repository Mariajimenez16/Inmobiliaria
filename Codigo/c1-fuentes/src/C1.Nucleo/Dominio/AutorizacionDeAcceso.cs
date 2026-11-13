namespace C1.Nucleo.Dominio;

// Entidad DENTRO del agregado Fuente. Constancia de quien autorizo, y cuando, el acceso de solo lectura a la estructura.
// NO es el permiso de un usuario en la herramienta (eso es de C7), ni la autorizacion de un titular (V6.a, V7).
// SOLID - SRP: solo conoce su propio ciclo de vida (vigente -> revocada). Las reglas de la fuente las aplica Fuente.
public sealed class AutorizacionDeAcceso
{
    private AutorizacionDeAcceso(Guid id, FuenteId fuenteId, string autorizador, DateTimeOffset autorizadaEn, DateTimeOffset? revocadaEn)
    {
        Id = id;
        FuenteId = fuenteId;
        Autorizador = autorizador;
        AutorizadaEn = autorizadaEn;
        RevocadaEn = revocadaEn;
    }

    public Guid Id { get; }
    public FuenteId FuenteId { get; }
    public string Autorizador { get; }
    public DateTimeOffset AutorizadaEn { get; }
    public DateTimeOffset? RevocadaEn { get; private set; }
    public bool Vigente => RevocadaEn is null;

    // Solo el agregado puede crearla y revocarla (internal): nadie fuera de Fuente salta sus reglas.
    internal static AutorizacionDeAcceso Otorgar(FuenteId fuenteId, string autorizador, DateTimeOffset ahora) =>
        new(Guid.NewGuid(), fuenteId, autorizador, ahora, null);

    internal void Revocar(DateTimeOffset ahora) => RevocadaEn = ahora;

    // Para que el adaptador de persistencia reconstruya una autorizacion ya guardada (no levanta eventos).
    public static AutorizacionDeAcceso Reconstituir(
        Guid id, FuenteId fuenteId, string autorizador, DateTimeOffset autorizadaEn, DateTimeOffset? revocadaEn) =>
        new(id, fuenteId, autorizador, autorizadaEn, revocadaEn);
}