namespace C1.Nucleo.Dominio;

// Objeto de valor: identidad estable de la fuente (invariante de la V6.a).
// Es un tipo propio (no un Guid suelto) para que nadie confunda el id de una fuente con otro id.
public readonly record struct FuenteId(Guid Valor)
{
    public static FuenteId Nueva() => new(Guid.NewGuid());

    public static FuenteId Desde(string? texto)
    {
        if (!Guid.TryParse(texto, out var guid) || guid == Guid.Empty)
            throw new ErrorDeValidacion("El identificador de la fuente no es valido.");
        return new FuenteId(guid);
    }

    public override string ToString() => Valor.ToString();
}