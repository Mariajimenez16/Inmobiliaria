namespace C1.Cliente;

// Entrada y salida de texto. Es una interfaz para que el menu y las demostraciones no dependan del teclado real.
public interface IConsola
{
    string? Leer(string pregunta);
    void Escribir(string texto);
}

public sealed class ConsolaReal : IConsola
{
    public string? Leer(string pregunta)
    {
        Console.Write(pregunta);
        return Console.ReadLine();
    }

    public void Escribir(string texto) => Console.WriteLine(texto);
}