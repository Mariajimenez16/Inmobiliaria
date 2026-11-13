using System.Collections.Concurrent;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Cache;

// Cache en memoria con tiempo de vida. Usa IReloj (no DateTime.Now) para poder probar la expiracion.
// SOLID - SRP: solo guarda y expira valores; no sabe de repositorios ni de dominio.
public sealed class CacheEnMemoria : ICacheDeLecturas
{
    private readonly ConcurrentDictionary<string, (object Valor, DateTimeOffset Expira)> _datos = new();
    private readonly TimeSpan _vida;
    private readonly IReloj _reloj;
    private int _aciertos;
    private int _fallos;

    public CacheEnMemoria(TimeSpan vida, IReloj reloj)
    {
        _vida = vida;
        _reloj = reloj;
    }

    public int Aciertos => Volatile.Read(ref _aciertos);
    public int Fallos => Volatile.Read(ref _fallos);

    public T? Obtener<T>(string clave) where T : class
    {
        if (_datos.TryGetValue(clave, out var entrada))
        {
            if (entrada.Expira > _reloj.Ahora && entrada.Valor is T valor)
            {
                Interlocked.Increment(ref _aciertos);
                return valor;
            }
            _datos.TryRemove(clave, out _);
        }

        Interlocked.Increment(ref _fallos);
        return null;
    }

    public void Poner<T>(string clave, T valor) where T : class =>
        _datos[clave] = (valor, _reloj.Ahora + _vida);
}