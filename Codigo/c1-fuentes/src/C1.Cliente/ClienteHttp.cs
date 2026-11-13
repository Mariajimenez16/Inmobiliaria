using System.Net.Http.Json;
using System.Text.Json;

namespace C1.Cliente;

// Lo que respondio el servidor. Estado 0 significa que no hubo respuesta (servicio apagado, URL incorrecta, tiempo agotado).
public sealed record Respuesta(int Estado, string Cuerpo)
{
    public bool Exitosa => Estado is >= 200 and < 300;

    public JsonElement? Json()
    {
        if (string.IsNullOrWhiteSpace(Cuerpo))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(Cuerpo);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Lee una propiedad de texto de la raiz del JSON (por ejemplo "fuenteId"). Devuelve null si no existe.
    public string? Texto(string propiedad)
    {
        var json = Json();
        if (json is { ValueKind: JsonValueKind.Object } raiz && raiz.TryGetProperty(propiedad, out var valor))
            return valor.ToString();
        return null;
    }
}

// SOLID - SRP: solo sabe enviar una solicitud HTTP. No conoce las reglas de C1 ni de C2.
public sealed class ClienteHttp
{
    private readonly HttpClient _http;

    public ClienteHttp(string urlBase)
    {
        UrlBase = urlBase.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(UrlBase + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public string UrlBase { get; }

    public async Task<Respuesta> Enviar(HttpMethod metodo, string ruta, object? cuerpo = null, string? usuario = null)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta.TrimStart('/'));
        if (usuario is not null)
            solicitud.Headers.Add("X-Usuario", usuario);
        if (cuerpo is not null)
            solicitud.Content = JsonContent.Create(cuerpo);

        try
        {
            using var respuesta = await _http.SendAsync(solicitud);
            return new Respuesta((int)respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            return new Respuesta(0, $"{ex.Message} ({UrlBase})");
        }
        catch (TaskCanceledException)
        {
            return new Respuesta(0, $"Tiempo de espera agotado ({UrlBase}).");
        }
    }
}