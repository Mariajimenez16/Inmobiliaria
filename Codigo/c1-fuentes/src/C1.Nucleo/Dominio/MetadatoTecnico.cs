namespace C1.Nucleo.Dominio;

// Objeto de valor: descripcion de la estructura de UNA columna (glosario V7). Solo estructura, nunca datos de personas.
// Dos metadatos con los mismos valores son iguales (por eso es un "record").
public sealed record MetadatoTecnico
{
    public MetadatoTecnico(string tabla, string columna, string tipoDeDato, int? longitud, bool obligatorio)
    {
        Tabla = Guarda.TextoObligatorio(tabla, "tabla");
        Columna = Guarda.TextoObligatorio(columna, "columna");
        TipoDeDato = Guarda.TextoObligatorio(tipoDeDato, "tipoDeDato", 100);
        if (longitud is < 0)
            throw new ErrorDeValidacion("La longitud no puede ser negativa.");
        Longitud = longitud;
        Obligatorio = obligatorio;
    }

    public string Tabla { get; }
    public string Columna { get; }
    public string TipoDeDato { get; }
    public int? Longitud { get; }
    public bool Obligatorio { get; }

    // Identifica a la columna dentro de una version ("tabla.columna"), sin distinguir mayusculas.
    public string Clave => $"{Tabla}.{Columna}";
}