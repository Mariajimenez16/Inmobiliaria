using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Entrada;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace C1.Adaptadores.Entrada.Http;

public sealed record RegistrarAvisoSolicitud(string Proveedor, string Descripcion, string? FuenteId);

// ADAPTADOR A3 (entrada): recibe el aviso de cambio que envia el proveedor. Solo traduce HTTP -> comando.
// SOLID - SRP: archivo propio, no mezcla este adaptador con las rutas de fuentes. OCP: se suma sin tocar FuentesEndpoints.
public static class AvisosEndpoints
{
    public static IEndpointRouteBuilder MapearAvisos(this IEndpointRouteBuilder app)
    {
        var avisos = app.MapGroup("/v1/avisos-de-cambio");
        avisos.MapPost("/", RegistrarAsync);
        avisos.MapGet("/", ListarAsync);
        return app;
    }

    private static FuenteId? LeerFuente(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : FuenteId.Desde(texto);

    private static async Task<IResult> RegistrarAsync(
        IMediador mediador, RegistrarAvisoSolicitud solicitud, CancellationToken ct)
    {
        var id = await mediador.Enviar(
            new RegistrarAvisoDeCambioComando(solicitud.Proveedor, solicitud.Descripcion, LeerFuente(solicitud.FuenteId)), ct);
        return Results.Created($"/v1/avisos-de-cambio/{id}", new { avisoId = id });
    }

    private static async Task<IResult> ListarAsync(IMediador mediador, string? fuenteId, CancellationToken ct) =>
        Results.Ok(await mediador.Consultar(new ListarAvisosConsulta(LeerFuente(fuenteId)), ct));
}