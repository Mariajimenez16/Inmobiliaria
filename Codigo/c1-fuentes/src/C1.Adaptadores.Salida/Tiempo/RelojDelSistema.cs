using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Tiempo;

// Adaptador del puerto IReloj. En las pruebas se reemplaza por un reloj fijo (por eso existe el puerto).
public sealed class RelojDelSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}