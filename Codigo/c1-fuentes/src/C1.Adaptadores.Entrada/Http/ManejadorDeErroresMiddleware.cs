using System.Text.Json;
using C1.Nucleo.Dominio;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace C1.Adaptadores.Entrada.Http;

// Traduce los errores del nucleo (ErrorDeAplicacion) a respuestas HTTP con formato ProblemDetails (diapositiva 9b).
// El nucleo no sabe que existe HTTP: la categoria del error se convierte en codigo aqui, en el adaptador.
//   Validacion -> 400 | PermisoDenegado -> 403 | NoEncontrado -> 404 | Conflicto -> 409
//   ReglaDeNegocio -> 422 | FuenteNoDisponible -> 503 | cualquier otra cosa -> 500 (sin filtrar detalles internos)
// SOLID - SRP: solo traduce errores. OCP: una categoria nueva se agrega en un solo lugar (EstadoDe).
public sealed class ManejadorDeErroresMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ManejadorDeErroresMiddleware> _log;

    public ManejadorDeErroresMiddleware(RequestDelegate siguiente, ILogger<ManejadorDeErroresMiddleware> log)
    {
        _siguiente = siguiente;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _siguiente(contexto);
        }
        catch (ErrorDeAplicacion error)
        {
            await Responder(contexto, EstadoDe(error.Categoria), error.Titulo, error.Detalle);
        }
        catch (BadHttpRequestException error)
        {
            await Responder(contexto, StatusCodes.Status400BadRequest, "Solicitud mal formada", error.Message);
        }
        catch (JsonException error)
        {
            await Responder(contexto, StatusCodes.Status400BadRequest, "Solicitud mal formada", error.Message);
        }
        catch (Exception error)
        {
            _log.LogError(error, "Error no controlado en {Ruta}", contexto.Request.Path);
            await Responder(contexto, StatusCodes.Status500InternalServerError, "Error interno",
                "Ocurrio un error inesperado. Intente de nuevo.");
        }
    }

    private static int EstadoDe(CategoriaDeError categoria) => categoria switch
    {
        CategoriaDeError.Validacion => StatusCodes.Status400BadRequest,
        CategoriaDeError.PermisoDenegado => StatusCodes.Status403Forbidden,
        CategoriaDeError.NoEncontrado => StatusCodes.Status404NotFound,
        CategoriaDeError.Conflicto => StatusCodes.Status409Conflict,
        CategoriaDeError.ReglaDeNegocio => StatusCodes.Status422UnprocessableEntity,
        CategoriaDeError.FuenteNoDisponible => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private static async Task Responder(HttpContext contexto, int estado, string titulo, string detalle)
    {
        var problema = new ProblemDetails
        {
            Status = estado,
            Title = titulo,
            Detail = detalle,
            Instance = contexto.Request.Path
        };

        contexto.Response.StatusCode = estado;
        await contexto.Response.WriteAsJsonAsync(problema, (JsonSerializerOptions?)null, "application/problem+json");
    }
}