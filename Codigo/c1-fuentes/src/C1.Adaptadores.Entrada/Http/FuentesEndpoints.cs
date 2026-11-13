using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace C1.Adaptadores.Entrada.Http;

// Cuerpos de las solicitudes HTTP (lo que llega por la red). No son comandos: el adaptador los traduce.
public sealed record RegistrarFuenteSolicitud(string Nombre, TipoDeFuente? Tipo, string? Credenciales);

public sealed record AutorizarAccesoSolicitud(string Autorizador);

public sealed record CompararEstructurasSolicitud(int? VersionAnterior, int? VersionNueva);

// ADAPTADOR DE ENTRADA (lado que conduce, diapositivas 5-7 y 16): traduce HTTP -> comando/consulta y lo entrega al puerto IMediador.
// No tiene reglas de negocio: solo lee la solicitud, arma el mensaje y devuelve el codigo HTTP.
// El usuario llega en el encabezado X-Usuario (representa la identidad que en el sistema real daria la autenticacion; ver registro de cambios).
// SOLID - SRP: traduccion HTTP, nada mas. DIP: depende del puerto IMediador, no de los manejadores.
public static class FuentesEndpoints
{
    public const string EncabezadoDeUsuario = "X-Usuario";

    public static IEndpointRouteBuilder MapearFuentes(this IEndpointRouteBuilder app)
    {
        var fuentes = app.MapGroup("/v1/fuentes");

        // Comandos
        fuentes.MapPost("/", RegistrarFuenteAsync);
        fuentes.MapPost("/{id}/autorizaciones", AutorizarAccesoAsync);
        fuentes.MapDelete("/{id}/autorizacion", RevocarAutorizacionAsync);
        fuentes.MapPost("/{id}/importaciones", ImportarAsync);
        fuentes.MapPost("/{id}/comparaciones", CompararAsync);
        fuentes.MapPost("/{id}/reintentos-de-importacion", ReintentarImportacionAsync);
        fuentes.MapPut("/{id}/cambios/{cambioId:guid}/revision", MarcarCambioRevisadoAsync);

        // Consultas
        fuentes.MapGet("/", ListarFuentesAsync);
        fuentes.MapGet("/{id}", ObtenerFuenteAsync);
        fuentes.MapGet("/{id}/versiones", ListarVersionesAsync);
        fuentes.MapGet("/{id}/versiones/{numero:int}", ObtenerVersionAsync);
        fuentes.MapGet("/{id}/cambios", ListarCambiosAsync);
        fuentes.MapGet("/{id}/estado-importacion", ObtenerEstadoDeImportacionAsync);

        return app;
    }

    private static string Usuario(HttpContext contexto) => contexto.Request.Headers[EncabezadoDeUsuario].ToString();

    private static async Task<IResult> RegistrarFuenteAsync(
        HttpContext contexto, IMediador mediador, RegistrarFuenteSolicitud solicitud, CancellationToken ct)
    {
        var tipo = solicitud.Tipo ?? throw new ErrorDeValidacion("El tipo de fuente es obligatorio.");
        var id = await mediador.Enviar(
            new RegistrarFuenteComando(Usuario(contexto), solicitud.Nombre, tipo, solicitud.Credenciales), ct);
        return Results.Created($"/v1/fuentes/{id}", new { fuenteId = id.ToString() });
    }

    private static async Task<IResult> AutorizarAccesoAsync(
        HttpContext contexto, IMediador mediador, string id, AutorizarAccesoSolicitud solicitud, CancellationToken ct)
    {
        var autorizacionId = await mediador.Enviar(
            new AutorizarAccesoAMetadatosComando(Usuario(contexto), FuenteId.Desde(id), solicitud.Autorizador), ct);
        return Results.Created($"/v1/fuentes/{id}", new { autorizacionId });
    }

    private static async Task<IResult> RevocarAutorizacionAsync(
        HttpContext contexto, IMediador mediador, string id, CancellationToken ct)
    {
        await mediador.Enviar(new RevocarAutorizacionDeMetadatosComando(Usuario(contexto), FuenteId.Desde(id)), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ImportarAsync(
        HttpContext contexto, IMediador mediador, string id, CancellationToken ct)
    {
        var resultado = await mediador.Enviar(
            new ImportarMetadatosTecnicosComando(Usuario(contexto), FuenteId.Desde(id)), ct);
        return Results.Created($"/v1/fuentes/{id}/versiones/{resultado.NumeroDeVersion}", resultado);
    }

    // El cuerpo es opcional: sin cuerpo se comparan las dos versiones mas recientes.
    private static async Task<IResult> CompararAsync(
        HttpContext contexto, IMediador mediador, string id, CompararEstructurasSolicitud? solicitud, CancellationToken ct)
    {
        var resultado = await mediador.Enviar(
            new CompararEstructurasComando(
                Usuario(contexto), FuenteId.Desde(id), solicitud?.VersionAnterior, solicitud?.VersionNueva), ct);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> ListarFuentesAsync(IMediador mediador, string? tipo, CancellationToken ct)
    {
        TipoDeFuente? filtro = null;
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            if (!Enum.TryParse<TipoDeFuente>(tipo, ignoreCase: true, out var valor))
                throw new ErrorDeValidacion(
                    $"Tipo de fuente desconocido: '{tipo}'. Valores validos: {string.Join(", ", Enum.GetNames<TipoDeFuente>())}.");
            filtro = valor;
        }

        return Results.Ok(await mediador.Consultar(new ListarFuentesConsulta(filtro), ct));
    }

    private static async Task<IResult> ObtenerFuenteAsync(IMediador mediador, string id, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ObtenerFuenteConsulta(FuenteId.Desde(id)), ct));

    private static async Task<IResult> ListarVersionesAsync(IMediador mediador, string id, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ListarVersionesConsulta(FuenteId.Desde(id)), ct));

    private static async Task<IResult> ObtenerVersionAsync(
        IMediador mediador, string id, int numero, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ObtenerVersionConsulta(FuenteId.Desde(id), numero), ct));

    private static async Task<IResult> ListarCambiosAsync(IMediador mediador, string id, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ListarCambiosConsulta(FuenteId.Desde(id)), ct));

        private static async Task<IResult> ReintentarImportacionAsync(
        HttpContext contexto, IMediador mediador, string id, CancellationToken ct)
    {
        var resultado = await mediador.Enviar(
            new ReintentarImportacionComando(Usuario(contexto), FuenteId.Desde(id)), ct);
        return Results.Created($"/v1/fuentes/{id}/versiones/{resultado.NumeroDeVersion}", resultado);
    }

    private static async Task<IResult> MarcarCambioRevisadoAsync(
        HttpContext contexto, IMediador mediador, string id, Guid cambioId, CancellationToken ct)
    {
        await mediador.Enviar(new MarcarCambioRevisadoComando(Usuario(contexto), FuenteId.Desde(id), cambioId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ObtenerEstadoDeImportacionAsync(IMediador mediador, string id, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ObtenerEstadoDeImportacionConsulta(FuenteId.Desde(id)), ct));
}