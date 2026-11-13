using System.Text;
using System.Text.Json;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Mensajeria;

public sealed record MensajePublicado(string Tipo, string Json, DateTimeOffset PublicadoEn);

public sealed record MensajeSalida(string Tipo, object Contrato);

// Un destino es un contexto que recibe mensajes (C2, C6...). OCP: agregar otro destino no toca el publicador.
public interface IDestinoDeMensajes
{
    string Nombre { get; }
    bool Interesa(string tipoDeMensaje);
    Task Entregar(MensajePublicado mensaje, CancellationToken ct);
}

// Destino SIMULADO: guarda en memoria lo que recibe (representa a C6, o a C2 si no esta levantado).
public sealed class DestinoEnMemoria : IDestinoDeMensajes
{
    private readonly HashSet<string> _tipos;
    private readonly List<MensajePublicado> _recibidos = new();
    private readonly object _candado = new();

    public DestinoEnMemoria(string nombre, IEnumerable<string> tiposQueRecibe)
    {
        Nombre = nombre;
        _tipos = new HashSet<string>(tiposQueRecibe);
    }

    public string Nombre { get; }

    public IReadOnlyList<MensajePublicado> Recibidos
    {
        get
        {
            lock (_candado) return _recibidos.ToList();
        }
    }

    public bool Interesa(string tipoDeMensaje) => _tipos.Contains(tipoDeMensaje);

    public Task Entregar(MensajePublicado mensaje, CancellationToken ct)
    {
        lock (_candado) _recibidos.Add(mensaje);
        return Task.CompletedTask;
    }
}

// Destino REAL por HTTP: entrega el mensaje al endpoint de otro servicio (por ejemplo, C2 en el puerto 8012).
public sealed class DestinoHttp : IDestinoDeMensajes
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly HashSet<string> _tipos;

    public DestinoHttp(string nombre, HttpClient http, string url, IEnumerable<string> tiposQueRecibe)
    {
        Nombre = nombre;
        _http = http;
        _url = url;
        _tipos = new HashSet<string>(tiposQueRecibe);
    }

    public string Nombre { get; }

    public bool Interesa(string tipoDeMensaje) => _tipos.Contains(tipoDeMensaje);

    public async Task Entregar(MensajePublicado mensaje, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(5));

        using var contenido = new StringContent(mensaje.Json, Encoding.UTF8, "application/json");
        using var respuesta = await _http.PostAsync(_url, contenido, limite.Token);
        respuesta.EnsureSuccessStatusCode();
    }
}

// Adaptador del puerto IPublicadorDeEventos.
// SOLID - SRP: busca el traductor del evento y reparte a los destinos; no decide NADA de negocio.
// OCP: nuevos eventos (traductores) y nuevos destinos sin modificar esta clase. DIP: depende de ITraductorDeEvento e IDestinoDeMensajes.
// Una entrega fallida NO rompe el comando (el cambio ya esta guardado): se anota en EntregasFallidas.
// Limitacion declarada: es "a lo sumo una vez" (sin Outbox). El Outbox esta en C2, el microservicio donde lo exige la diapositiva 9a.
public sealed class PublicadorDeEventos : IPublicadorDeEventos
{
    private readonly IReadOnlyList<IDestinoDeMensajes> _destinos;
    private readonly IReadOnlyList<ITraductorDeEvento> _traductores;
    private readonly IReloj _reloj;
    private readonly List<MensajePublicado> _publicados = new();
    private readonly List<string> _fallidas = new();
    private readonly object _candado = new();

    public PublicadorDeEventos(
        IEnumerable<IDestinoDeMensajes> destinos,
        IEnumerable<ITraductorDeEvento> traductores,
        IReloj reloj)
    {
        _destinos = destinos.ToList();
        _traductores = traductores.ToList();
        _reloj = reloj;
    }

    public IReadOnlyList<MensajePublicado> MensajesPublicados
    {
        get
        {
            lock (_candado) return _publicados.ToList();
        }
    }

    public IReadOnlyList<string> EntregasFallidas
    {
        get
        {
            lock (_candado) return _fallidas.ToList();
        }
    }

    public async Task Publicar(IEnumerable<EventoDeDominio> eventos, CancellationToken ct)
    {
        foreach (var evento in eventos)
        {
            var traductor = _traductores.FirstOrDefault(t => t.Atiende(evento));
            if (traductor is null)
                continue; // evento interno sin contrato publicado

            var salida = traductor.Traducir(evento);
            var json = JsonSerializer.Serialize(salida.Contrato, salida.Contrato.GetType());
            var mensaje = new MensajePublicado(salida.Tipo, json, _reloj.Ahora);
            lock (_candado) _publicados.Add(mensaje);

            foreach (var destino in _destinos.Where(d => d.Interesa(mensaje.Tipo)))
            {
                try
                {
                    await destino.Entregar(mensaje, ct);
                }
                catch (Exception ex)
                {
                    lock (_candado) _fallidas.Add($"{destino.Nombre} <- {mensaje.Tipo}: {ex.Message}");
                }
            }
        }
    }
}