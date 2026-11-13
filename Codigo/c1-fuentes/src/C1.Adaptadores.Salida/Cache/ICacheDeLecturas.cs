namespace C1.Adaptadores.Salida.Cache;

// Contrato minimo de una cache de lecturas. SOLID - ISP: solo lo que se usa. DIP: el decorador depende de esta interfaz, no de la clase concreta.
public interface ICacheDeLecturas
{
    T? Obtener<T>(string clave) where T : class;
    void Poner<T>(string clave, T valor) where T : class;
    int Aciertos { get; }
    int Fallos { get; }
}