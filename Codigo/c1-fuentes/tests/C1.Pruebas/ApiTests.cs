using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace C1.Pruebas;

// Arranca la aplicacion REAL (con el Host y SQLite en un archivo temporal) dentro de la prueba, sin abrir puertos.
public sealed class FabricaDeApi : WebApplicationFactory<Program>
{
    public string Ruta { get; } = Path.Combine(Path.GetTempPath(), $"c1-api-{Guid.NewGuid():N}.db");

    public FabricaDeApi()
    {
        Environment.SetEnvironmentVariable("C1_RUTA_BD", Ruta);
        Environment.SetEnvironmentVariable("C1_CLAVE_CIFRADO", "clave-de-pruebas");
        Environment.SetEnvironmentVariable("C2_URL", null); // C2 simulado en memoria
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(Ruta); } catch (IOException) { }
    }
}

public class ApiTests : IClassFixture<FabricaDeApi>
{
    private readonly FabricaDeApi _fabrica;

    public ApiTests(FabricaDeApi fabrica) => _fabrica = fabrica;

    private HttpClient Cliente(string? usuario)
    {
        var cliente = _fabrica.CreateClient();
        if (usuario is not null)
            cliente.DefaultRequestHeaders.Add("X-Usuario", usuario);
        return cliente;
    }

    private static async Task<string> RegistrarFuente(HttpClient c, string tipo, string? credenciales)
    {
        var respuesta = await c.PostAsJsonAsync("/v1/fuentes", new { nombre = "S-" + Guid.NewGuid().ToString("N")[..6], tipo, credenciales });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("fuenteId").GetString()!;
    }

    private static async Task Autorizar(HttpClient c, string id)
    {
        var respuesta = await c.PostAsJsonAsync($"/v1/fuentes/{id}/autorizaciones", new { autorizador = "Jefa de administracion" });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    private static object Estructura(params (string tabla, string columna, string tipo)[] columnas) => new
    {
        columnas = columnas.Select(x => new { x.tabla, x.columna, tipoDeDato = x.tipo, longitud = (int?)null, obligatorio = true })
    };

    [Fact]
    public async Task Salud_responde_ok()
    {
        var respuesta = await Cliente(null).GetAsync("/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task Flujo_completo_registrar_autorizar_importar_cambiar_y_comparar()
    {
        var c = Cliente("admin");
        var id = await RegistrarFuente(c, "MotorRelacional", "usuario=ro;clave=123");
        await Autorizar(c, id);

        (await c.PutAsJsonAsync($"/v1/simulacion/fuentes/{id}/estructura",
            Estructura(("clientes", "cedula", "varchar"), ("clientes", "nombre", "varchar")))).EnsureSuccessStatusCode();
        var primera = await c.PostAsync($"/v1/fuentes/{id}/importaciones", null);
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(1, (await primera.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("numeroDeVersion").GetInt32());

        (await c.PutAsJsonAsync($"/v1/simulacion/fuentes/{id}/estructura",
            Estructura(("clientes", "cedula", "bigint"), ("clientes", "nombre", "varchar"), ("clientes", "correo", "varchar")))).EnsureSuccessStatusCode();
        var segunda = await c.PostAsync($"/v1/fuentes/{id}/importaciones", null);
        Assert.Equal(2, (await segunda.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("numeroDeVersion").GetInt32());

        var comparacion = await c.PostAsync($"/v1/fuentes/{id}/comparaciones", null);
        Assert.Equal(HttpStatusCode.OK, comparacion.StatusCode);
        Assert.True((await comparacion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hayCambio").GetBoolean());

        var cambios = await c.GetFromJsonAsync<JsonElement>($"/v1/fuentes/{id}/cambios");
        Assert.Equal(1, cambios.GetArrayLength());

        var fuente = await c.GetFromJsonAsync<JsonElement>($"/v1/fuentes/{id}");
        Assert.False(fuente.GetProperty("sinCubrir").GetBoolean());

        var publicados = (await c.GetStringAsync("/v1/simulacion/mensajes-publicados"));
        Assert.Contains("FuenteRegistrada.v1", publicados);
        Assert.Contains("MetadatosTecnicosImportados.v1", publicados);
        Assert.Contains("CambioDeEstructuraDetectado.v1", publicados);
    }

    [Fact]
    public async Task Sin_encabezado_de_usuario_responde_400_con_problem_details()
    {
        var respuesta = await Cliente(null).PostAsJsonAsync("/v1/fuentes",
            new { nombre = "S1", tipo = "MotorRelacional", credenciales = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Un_usuario_sin_permiso_recibe_403_y_queda_en_la_bitacora_de_C7()
    {
        var respuesta = await Cliente("consulta").PostAsJsonAsync("/v1/fuentes",
            new { nombre = "S1", tipo = "MotorRelacional", credenciales = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var bitacora = await Cliente("admin").GetStringAsync("/v1/simulacion/denegaciones-c7");
        Assert.Contains("consulta", bitacora);
    }

    [Fact]
    public async Task Una_fuente_inexistente_responde_404()
    {
        var respuesta = await Cliente("admin").GetAsync($"/v1/fuentes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Autorizar_dos_veces_responde_409()
    {
        var c = Cliente("admin");
        var id = await RegistrarFuente(c, "MotorRelacional", "usuario=ro");
        await Autorizar(c, id);

        var segunda = await c.PostAsJsonAsync($"/v1/fuentes/{id}/autorizaciones", new { autorizador = "Otra persona" });

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task Importar_del_CRM_antes_de_que_el_proveedor_entregue_responde_503()
    {
        var c = Cliente("ti");
        var id = await RegistrarFuente(c, "SistemaDeProveedor", null);
        await Autorizar(c, id);

        var respuesta = await c.PostAsync($"/v1/fuentes/{id}/importaciones", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
    }
}