namespace C1.Nucleo.Dominio;

// SOLID - SRP: solo sabe acumular eventos de dominio. El agregado los levanta; la capa de aplicacion los publica despues.
public abstract class RaizDeAgregado
{
    private readonly List<EventoDeDominio> _eventos = new();

    public IReadOnlyList<EventoDeDominio> Eventos => _eventos;

    protected void AgregarEvento(EventoDeDominio evento) => _eventos.Add(evento);

    // Entrega los eventos pendientes y vacia la lista (asi no se publican dos veces).
    public IReadOnlyList<EventoDeDominio> ExtraerEventos()
    {
        var copia = _eventos.ToArray();
        _eventos.Clear();
        return copia;
    }

        // Descarta los eventos levantados despues de un punto. Lo usa Restaurar: si se vuelve atras, esos eventos no ocurrieron.
    protected void DescartarEventosDesde(int cantidadAConservar)
    {
        if (cantidadAConservar < _eventos.Count)
            _eventos.RemoveRange(cantidadAConservar, _eventos.Count - cantidadAConservar);
    }
}