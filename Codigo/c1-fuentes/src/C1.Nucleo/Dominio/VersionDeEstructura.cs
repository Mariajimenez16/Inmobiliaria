namespace C1.Nucleo.Dominio;

// Fuera del agregado Fuente (V6.a): su ciclo de vida historico no depende de la fuente.
// Invariante: cada version identifica la fuente y la fecha de importacion.
// Es inmutable: una version importada no se modifica; si cambia la estructura, se importa OTRA version.
public sealed class VersionDeEstructura
{
    private VersionDeEstructura(Guid id, FuenteId fuenteId, int numero, DateTimeOffset importadaEn, IReadOnlyList<MetadatoTecnico> columnas)
    {
        Id = id;
        FuenteId = fuenteId;
        Numero = numero;
        ImportadaEn = importadaEn;
        Columnas = columnas;
    }

    public Guid Id { get; }
    public FuenteId FuenteId { get; }
    public int Numero { get; }
    public DateTimeOffset ImportadaEn { get; }
    public IReadOnlyList<MetadatoTecnico> Columnas { get; }

    public static VersionDeEstructura Crear(
        FuenteId fuenteId, int numero, DateTimeOffset importadaEn, IEnumerable<MetadatoTecnico> columnas)
    {
        if (numero < 1)
            throw new ErrorDeValidacion("El numero de version debe ser 1 o mayor.");

        var lista = columnas.ToList();
        if (lista.Count == 0)
            throw new ErrorDeReglaDeNegocio("La estructura importada no contiene columnas.");

        var repetida = lista.GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
            throw new ErrorDeReglaDeNegocio($"La columna '{repetida.Key}' aparece repetida en la estructura importada.");

        return new VersionDeEstructura(Guid.NewGuid(), fuenteId, numero, importadaEn, lista);
    }

    public static VersionDeEstructura Reconstituir(
        Guid id, FuenteId fuenteId, int numero, DateTimeOffset importadaEn, IEnumerable<MetadatoTecnico> columnas) =>
        new(id, fuenteId, numero, importadaEn, columnas.ToList());
}