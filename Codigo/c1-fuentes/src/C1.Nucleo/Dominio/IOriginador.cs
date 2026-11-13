namespace C1.Nucleo.Dominio;

// PATRON MEMENTO (diapositiva 9b). El originador sabe sacar una "foto" inmutable de su estado y volver a ella.
// SOLID - ISP: interfaz de dos metodos, nada mas. Misma forma que IOriginador de C2.
public interface IOriginador<TMemento>
{
    TMemento CrearMemento();
    void Restaurar(TMemento memento);
}