namespace C1.Nucleo.Dominio;

// SOLID - SRP: una sola responsabilidad, validar entradas basicas. Evita repetir los mismos "if" en cada clase.
public static class Guarda
{
    public static string TextoObligatorio(string? valor, string campo, int maximo = 200)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ErrorDeValidacion($"El campo '{campo}' es obligatorio.");

        var limpio = valor.Trim();
        if (limpio.Length > maximo)
            throw new ErrorDeValidacion($"El campo '{campo}' no puede superar {maximo} caracteres.");

        return limpio;
    }
}