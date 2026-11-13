using C1.Adaptadores.Salida.Mensajeria;
using C1.Adaptadores.Salida.Simulacion;
using C1.Nucleo.Dominio;

namespace C1.Host;

public sealed record ColumnaSimuladaSolicitud(string Tabla, string Columna, string TipoDeDato, int? Longitud, bool Obligatorio);

public sealed record EstructuraSimuladaSolicitud(List<ColumnaSimuladaSolicitud>? Columnas);

// Los destinos simulados en memoria, para poder consultarlos desde la API de simulacion.
// C2 solo es "en memoria" cuando no se configura C2_URL (si se configura, el mensaje viaja por HTTP real).
public sealed record DestinosSimulados(DestinoEnMemoria C6, DestinoEnMemoria? C2);

// ANDAMIAJE de la simulacion de interoperabilidad (no es parte del negocio ni del diseño):
// permite decidir que estructura "entrega" una fuente simulada y ver lo que C1 envio a otros contextos.
public static class SimulacionEndpoints
{
    public static IEndpointRouteBuilder MapearSimulacion(this IEndpointRouteBuilder app)
    {
        var simulacion = app.MapGroup("/v1/simulacion");

        // Define la estructura que la fuente "tiene" (A1) o que el proveedor "entrega" (A2). Permite probar cambios de estructura.
        simulacion.MapPut("/fuentes/{id}/estructura",
            (string id, EstructuraSimuladaSolicitud solicitud, EstructurasSimuladasDeFuentes estructuras) =>
            {
                var columnas = (solicitud.Columnas ?? throw new ErrorDeValidacion("Debe indicar las columnas."))
                    .Select(c => new MetadatoTecnico(c.Tabla, c.Columna, c.TipoDeDato, c.Longitud, c.Obligatorio))
                    .ToList();
                estructuras.Definir(FuenteId.Desde(id), columnas);
                return Results.NoContent();
            });

        // Todo lo que C1 publico (ya traducido a contratos V1), vaya o no un destino interesado.
        simulacion.MapGet("/mensajes-publicados", (PublicadorDeEventos publicador) =>
            Results.Ok(publicador.MensajesPublicados));

        simulacion.MapGet("/entregas-fallidas", (PublicadorDeEventos publicador) =>
            Results.Ok(publicador.EntregasFallidas));

        // Lo que "recibieron" C6 y (si no esta levantado) C2.
        simulacion.MapGet("/c6/recibidos", (DestinosSimulados destinos) => Results.Ok(destinos.C6.Recibidos));

        simulacion.MapGet("/c2/recibidos", (DestinosSimulados destinos) =>
            Results.Ok(destinos.C2?.Recibidos ?? (IReadOnlyList<MensajePublicado>)Array.Empty<MensajePublicado>()));

        // Bitacora de denegaciones de C7 (contrato 4.12: sin permiso, se deniega y se registra).
        simulacion.MapGet("/denegaciones-c7", (VerificadorDePermisosSimuladoC7 c7) => Results.Ok(c7.Denegaciones));

        return app;
    }
}