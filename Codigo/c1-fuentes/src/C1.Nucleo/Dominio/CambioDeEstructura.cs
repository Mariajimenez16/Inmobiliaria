namespace C1.Nucleo.Dominio;

// Fuera del agregado Fuente (V6.a). Diferencia detectada entre dos importaciones de UNA MISMA fuente.
// Invariantes: la comparacion es entre versiones de la misma fuente; solo se registra un cambio si HAY diferencia;
// un cambio se marca como revisado una sola vez (contrato 4.9, marcarRevisado).
// No modifica clasificaciones ni definiciones (glosario V7): solo deja constancia de la diferencia tecnica.
public sealed class CambioDeEstructura : RaizDeAgregado
{
    private CambioDeEstructura(
        Guid id, FuenteId fuenteId, int versionAnterior, int versionNueva,
        DateTimeOffset detectadoEn, IReadOnlyList<DiferenciaDeEstructura> diferencias)
    {
        Id = id;
        FuenteId = fuenteId;
        VersionAnterior = versionAnterior;
        VersionNueva = versionNueva;
        DetectadoEn = detectadoEn;
        Diferencias = diferencias;
    }

    public Guid Id { get; }
    public FuenteId FuenteId { get; }
    public int VersionAnterior { get; }
    public int VersionNueva { get; }
    public DateTimeOffset DetectadoEn { get; }
    public IReadOnlyList<DiferenciaDeEstructura> Diferencias { get; }

    // Revision (contrato 4.9): quien reviso el cambio y cuando. Mientras sea null, el cambio esta pendiente de revisar.
    public string? RevisadoPor { get; private set; }
    public DateTimeOffset? RevisadoEn { get; private set; }
    public bool Revisado => RevisadoEn is not null;

    // ---- Comando: Comparar estructuras -> evento Cambio de estructura detectado (SOLO si hay diferencias)
    // Devuelve null cuando las dos versiones son iguales: no hay cambio, no hay evento.
    public static CambioDeEstructura? Detectar(VersionDeEstructura anterior, VersionDeEstructura nueva, DateTimeOffset ahora)
    {
        if (anterior.FuenteId != nueva.FuenteId)
            throw new ErrorDeReglaDeNegocio("Solo se pueden comparar versiones de estructura de la misma fuente.");
        if (nueva.Numero <= anterior.Numero)
            throw new ErrorDeValidacion("La version nueva debe ser posterior a la anterior.");

        var diferencias = ComparadorDeEstructuras.Comparar(anterior, nueva);
        if (diferencias.Count == 0)
            return null;

        var cambio = new CambioDeEstructura(Guid.NewGuid(), nueva.FuenteId, anterior.Numero, nueva.Numero, ahora, diferencias);
        cambio.AgregarEvento(new CambioDeEstructuraDetectado(
            Guid.NewGuid(), ahora, cambio.FuenteId, cambio.Id, anterior.Numero, nueva.Numero, diferencias.Count));
        return cambio;
    }

    // ---- Comando: Marcar cambio como revisado (contrato 4.9). El diseno no define un evento para esto, asi que no se publica.
    public void MarcarRevisado(string revisor, DateTimeOffset ahora)
    {
        var nombre = Guarda.TextoObligatorio(revisor, "revisor");
        if (Revisado)
            throw new ErrorDeConflicto("Este cambio de estructura ya fue marcado como revisado.");

        RevisadoPor = nombre;
        RevisadoEn = ahora;
    }

    public static CambioDeEstructura Reconstituir(
        Guid id, FuenteId fuenteId, int versionAnterior, int versionNueva,
        DateTimeOffset detectadoEn, IEnumerable<DiferenciaDeEstructura> diferencias,
        string? revisadoPor = null, DateTimeOffset? revisadoEn = null)
    {
        var cambio = new CambioDeEstructura(id, fuenteId, versionAnterior, versionNueva, detectadoEn, diferencias.ToList());
        cambio.RevisadoPor = revisadoPor;
        cambio.RevisadoEn = revisadoEn;
        return cambio;
    }
}