namespace C1.Nucleo.Dominio;

// SOLID - SRP: el nucleo describe QUE error ocurrio; no sabe nada de HTTP (eso lo traduce el adaptador de entrada).
public enum CategoriaDeError
{
    Validacion,        // el adaptador la mapea a 400
    NoEncontrado,      // 404
    Conflicto,         // 409
    ReglaDeNegocio,    // 422
    PermisoDenegado,    // 403
    FuenteNoDisponible // 503
}

public abstract class ErrorDeAplicacion : Exception
{
    protected ErrorDeAplicacion(CategoriaDeError categoria, string titulo, string detalle) : base(detalle)
    {
        Categoria = categoria;
        Titulo = titulo;
        Detalle = detalle;
    }

    public CategoriaDeError Categoria { get; }
    public string Titulo { get; }
    public string Detalle { get; }
}

public sealed class ErrorDeValidacion : ErrorDeAplicacion
{
    public ErrorDeValidacion(string detalle) : base(CategoriaDeError.Validacion, "Solicitud invalida", detalle) { }
}

public sealed class ErrorNoEncontrado : ErrorDeAplicacion
{
    public ErrorNoEncontrado(string detalle) : base(CategoriaDeError.NoEncontrado, "Recurso no encontrado", detalle) { }
}

public sealed class ErrorDeConflicto : ErrorDeAplicacion
{
    public ErrorDeConflicto(string detalle) : base(CategoriaDeError.Conflicto, "Conflicto", detalle) { }
}

public sealed class ErrorDeReglaDeNegocio : ErrorDeAplicacion
{
    public ErrorDeReglaDeNegocio(string detalle) : base(CategoriaDeError.ReglaDeNegocio, "Regla de negocio violada", detalle) { }
}

public sealed class ErrorDePermisoDenegado : ErrorDeAplicacion
{
    public ErrorDePermisoDenegado(string detalle) : base(CategoriaDeError.PermisoDenegado, "Permiso denegado", detalle) { }
}

public sealed class ErrorDeFuenteNoDisponible : ErrorDeAplicacion
{
    public ErrorDeFuenteNoDisponible(string detalle) : base(CategoriaDeError.FuenteNoDisponible, "Fuente no disponible", detalle) { }
}