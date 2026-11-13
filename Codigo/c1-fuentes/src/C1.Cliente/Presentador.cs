using System.Text.Encodings.Web;
using System.Text.Json;

namespace C1.Cliente;

// SOLID - SRP: decide COMO se ve una respuesta en pantalla. No llama a la red ni toma decisiones de negocio.
public sealed class Presentador
{
    private static readonly JsonSerializerOptions Sangrado = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IConsola _consola;

    public Presentador(IConsola consola) => _consola = consola;

    public void Mostrar(Respuesta respuesta)
    {
        if (respuesta.Estado == 0)
        {
            _consola.Escribir($"  [X] Sin respuesta: {respuesta.Cuerpo}");
            _consola.Escribir("      Verifique que el servicio este levantado y que la URL sea la correcta.");
            return;
        }

        _consola.Escribir($"  [{(respuesta.Exitosa ? "OK" : "X")}] HTTP {respuesta.Estado} {Descripcion(respuesta.Estado)}");

        if (respuesta.Json() is not { } raiz)
            return;

        // Errores en formato ProblemDetails: se muestra "titulo: detalle".
        if (!respuesta.Exitosa && raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("title", out var titulo))
        {
            var detalle = raiz.TryGetProperty("detail", out var d) ? d.ToString() : "";
            _consola.Escribir($"      {titulo}: {detalle}");
            return;
        }

        foreach (var linea in JsonSerializer.Serialize(raiz, Sangrado).Split('\n'))
            _consola.Escribir("      " + linea.TrimEnd('\r'));
    }

    // Resumen de /v1/simulacion/mensajes-publicados: el tipo de cada mensaje y el JSON exacto (contrato V1) que viajo.
    public void MostrarMensajes(Respuesta respuesta)
    {
        if (!respuesta.Exitosa || respuesta.Json() is not { ValueKind: JsonValueKind.Array } lista)
        {
            Mostrar(respuesta);
            return;
        }

        _consola.Escribir($"  [OK] {lista.GetArrayLength()} mensaje(s)");
        foreach (var mensaje in lista.EnumerateArray())
        {
            var tipo = mensaje.TryGetProperty("tipo", out var t) ? t.ToString() : "?";
            var json = mensaje.TryGetProperty("json", out var j) ? j.ToString() : "";
            _consola.Escribir($"      - {tipo}");
            _consola.Escribir($"        {json}");
        }
    }

    private static string Descripcion(int estado) => estado switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => ""
    };
}