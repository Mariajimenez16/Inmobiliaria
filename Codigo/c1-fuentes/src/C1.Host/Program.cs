using System.Text.Json.Serialization;
using C1.Adaptadores.Entrada.Http;
using C1.Adaptadores.Salida.Cifrado;
using C1.Adaptadores.Salida.Mensajeria;
using C1.Adaptadores.Salida.Persistencia;
using C1.Adaptadores.Salida.Simulacion;
using C1.Adaptadores.Salida.Tiempo;
using C1.Contracts.V1;
using C1.Host;
using C1.Nucleo.Aplicacion;
using C1.Nucleo.Puertos.Entrada;
using C1.Nucleo.Puertos.Salida;
using C1.Adaptadores.Salida.Cache;

// RAIZ DE COMPOSICION (diapositivas 10 y 11): el unico lugar que conoce las clases concretas.
// Aqui cada puerto del nucleo se conecta con su adaptador. Cambiar SQLite por otra base, o C6 simulado por real,
// se hace aqui y no toca el nucleo (SOLID - DIP).
//
// Configuracion (variables de entorno):
//   C1_URLS            direccion de escucha (por defecto http://localhost:8011)
//   C1_RUTA_BD         archivo SQLite (por defecto datos/c1_fuentes.db)
//   C1_CLAVE_CIFRADO   secreto para cifrar credenciales (obligatorio salvo C1_ENTORNO=dev, que es el valor por defecto)
//   C2_URL             si se define (p. ej. http://localhost:8012), "Fuente registrada" se envia a C2 por HTTP real
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["C1_URLS"] ?? "http://localhost:8011");

var entorno = builder.Configuration["C1_ENTORNO"] ?? "dev";
var clave = builder.Configuration["C1_CLAVE_CIFRADO"];
if (string.IsNullOrWhiteSpace(clave))
{
    if (entorno != "dev")
        throw new InvalidOperationException("Falta C1_CLAVE_CIFRADO. Solo en C1_ENTORNO=dev se usa una clave de desarrollo.");
    clave = "clave-solo-para-desarrollo";
}

var rutaBd = builder.Configuration["C1_RUTA_BD"] ?? "datos/c1_fuentes.db";
var urlDeC2 = builder.Configuration["C2_URL"];

var servicios = builder.Services;

servicios.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// --- Puertos de salida -> adaptadores ---
var opciones = new OpcionesSqlite(rutaBd);
EsquemaSqlite.Inicializar(opciones);
servicios.AddSingleton(opciones);
servicios.AddSingleton<ICifrador>(new CifradorAesGcm(clave));
servicios.AddSingleton<IReloj, RelojDelSistema>();

// Cache de lecturas (Decorator sobre el repositorio de versiones). Vida configurable: C1_TTL_CACHE_SEGUNDOS (por defecto 60).
var segundosDeCache = int.TryParse(builder.Configuration["C1_TTL_CACHE_SEGUNDOS"], out var segundos) && segundos > 0 ? segundos : 60;
servicios.AddSingleton<ICacheDeLecturas>(sp =>
    new CacheEnMemoria(TimeSpan.FromSeconds(segundosDeCache), sp.GetRequiredService<IReloj>()));

// Una unidad de trabajo por solicitud: si algo falla antes de Confirmar, se hace rollback al liberarla.
servicios.AddScoped<UnidadDeTrabajoSqlite>();
servicios.AddScoped<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoSqlite>());

// Simulaciones: lectores A1/A2 de las fuentes y servicio de permisos de C7.
servicios.AddSingleton<EstructurasSimuladasDeFuentes>();
servicios.AddSingleton<ILectorDeEstructura, LectorA1MotorRelacional>();
servicios.AddSingleton<ILectorDeEstructura, LectorA2Crm>();
servicios.AddSingleton<VerificadorDePermisosSimuladoC7>();
servicios.AddSingleton<IVerificadorDePermisos>(sp => sp.GetRequiredService<VerificadorDePermisosSimuladoC7>());

// Publicacion de eventos: C6 siempre simulado; C2 por HTTP real si hay C2_URL, si no, simulado en memoria.
var c6 = new DestinoEnMemoria("C6", new[] { MetadatosTecnicosImportadosV1.Tipo, CambioDeEstructuraDetectadoV1.Tipo });
var tiposParaC2 = new[] { FuenteRegistradaV1.Tipo };
DestinoEnMemoria? c2EnMemoria = null;
IDestinoDeMensajes destinoC2;
if (!string.IsNullOrWhiteSpace(urlDeC2))
{
    destinoC2 = new DestinoHttp(
        "C2", new HttpClient(), $"{urlDeC2.TrimEnd('/')}/v1/integracion/fuentes-registradas", tiposParaC2);
}
else
{
    c2EnMemoria = new DestinoEnMemoria("C2", tiposParaC2);
    destinoC2 = c2EnMemoria;
}

var publicador = new PublicadorDeEventos(
    new[] { destinoC2, (IDestinoDeMensajes)c6 }, TraductoresDeEventos.Todos(), new RelojDelSistema());
servicios.AddSingleton(publicador);
servicios.AddSingleton<IPublicadorDeEventos>(publicador);
servicios.AddSingleton(new DestinosSimulados(c6, c2EnMemoria));

// --- Nucleo: manejadores (se registran por convencion: todo IManejadorDeComando/IManejadorDeConsulta del nucleo) ---
foreach (var tipo in typeof(Mediador).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
{
    foreach (var contrato in tipo.GetInterfaces().Where(i => i.IsGenericType &&
                 (i.GetGenericTypeDefinition() == typeof(IManejadorDeComando<,>) ||
                  i.GetGenericTypeDefinition() == typeof(IManejadorDeConsulta<,>))))
    {
        servicios.AddScoped(contrato, tipo);
    }
}

// Pipeline de comandos (en orden: el primero es el mas externo) y puerto de entrada.
servicios.AddScoped<IPasoDeComando, VerificacionDePermisosPaso>();
servicios.AddScoped<IMediador>(sp => new Mediador(sp, sp.GetServices<IPasoDeComando>()));

var app = builder.Build();

app.UseMiddleware<ManejadorDeErroresMiddleware>();
app.MapGet("/salud", () => Results.Ok(new { estado = "ok", servicio = "c1-fuentes" }));
app.MapearFuentes();
app.MapearAvisos();
app.MapearSimulacion();
app.MapGet("/v1/simulacion/cache", (ICacheDeLecturas cache) =>
    Results.Ok(new { aciertos = cache.Aciertos, fallos = cache.Fallos }));

app.Run();

// Necesario para que las pruebas puedan arrancar la aplicacion en memoria (WebApplicationFactory<Program>).
public partial class Program { }