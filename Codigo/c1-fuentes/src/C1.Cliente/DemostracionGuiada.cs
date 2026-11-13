using System.Text.Json;

namespace C1.Cliente;

// Dos guiones para la sustentacion. Cada paso explica que se va a hacer y espera Enter, para poder narrarlo.
// SOLID - SRP: solo orquesta llamadas de demostracion; las reglas que se ven (403, 409, 422, 503) son de C1 y de C2.
public sealed class DemostracionGuiada
{
    private readonly ClienteHttp _c1;
    private readonly ClienteHttp _c2;
    private readonly Presentador _presentador;
    private readonly IConsola _consola;
    private int _paso;

    public DemostracionGuiada(ClienteHttp c1, ClienteHttp c2, Presentador presentador, IConsola consola)
    {
        _c1 = c1;
        _c2 = c2;
        _presentador = presentador;
        _consola = consola;
    }

    private void Titulo(string texto)
    {
        _paso = 0;
        _consola.Escribir("");
        _consola.Escribir($"=== {texto} ===");
    }

    private void Paso(string titulo, string explicacion)
    {
        _paso++;
        _consola.Escribir("");
        _consola.Escribir($"--- Paso {_paso}: {titulo} ---");
        _consola.Escribir($"    {explicacion}");
        _consola.Leer("    (Enter para ejecutar) ");
    }

    private async Task<Respuesta> Llamar(
        ClienteHttp cliente, HttpMethod metodo, string ruta, object? cuerpo = null, string? usuario = null)
    {
        var respuesta = await cliente.Enviar(metodo, ruta, cuerpo, usuario);
        _presentador.Mostrar(respuesta);
        return respuesta;
    }

    // =====================================================================================================
    // Demostracion 1: solo C1
    // =====================================================================================================
    public async Task DeC1()
    {
        Titulo("Demostracion de C1 - Fuentes y metadatos tecnicos (arquitectura hexagonal)");

        Paso("Salud de C1", "Comprobamos que la API de C1 (adaptador de entrada) responde.");
        if (!(await Llamar(_c1, HttpMethod.Get, "/salud")).Exitosa) return;

        Paso("Registrar la fuente S1 (motor relacional)",
            "Comando RegistrarFuente. C1 guarda la fuente en SQLite y cifra con AES-GCM la cuenta de solo lectura. Emite 'Fuente registrada'.");
        var s1 = (await Llamar(_c1, HttpMethod.Post, "/v1/fuentes",
            new { nombre = "S1 Contabilidad", tipo = "MotorRelacional", credenciales = "usuario=ro;clave=123" }, "admin")).Texto("fuenteId");
        if (s1 is null) return;

        Paso("Intentar importar SIN autorizacion",
            "Regla de la V6.a: solo se importa con autorizacion. El nucleo rechaza el comando; el adaptador lo traduce a un codigo HTTP.");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/importaciones", null, "admin");

        Paso("Autorizar el acceso a los metadatos",
            "La autorizacion registra QUIEN autoriza (invariante de la V6.a). Emite 'Acceso a metadatos autorizado'.");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/autorizaciones", new { autorizador = "Jefa de administracion" }, "admin");

        Paso("Autorizar por segunda vez",
            "Decision de implementacion registrada en el diseño (#1): una fuente no puede tener dos autorizaciones vigentes.");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/autorizaciones", new { autorizador = "Otra persona" }, "admin");

        Paso("Importar la estructura (version 1)",
            "Simulamos que S1 tiene la estructura base y el adaptador A1 la lee (solo estructura, nunca registros). Emite 'Metadatos tecnicos importados'.");
        await Llamar(_c1, HttpMethod.Put, $"/v1/simulacion/fuentes/{s1}/estructura", Estructuras.Base());
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/importaciones", null, "ti");

        Paso("La fuente cambia y se vuelve a importar (version 2)",
            "Simulamos que alguien cambio la base: otro tipo en cedula, una columna menos y una nueva.");
        await Llamar(_c1, HttpMethod.Put, $"/v1/simulacion/fuentes/{s1}/estructura", Estructuras.Modificada());
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/importaciones", null, "ti");

        Paso("Comparar las dos versiones",
            "Solo se compara dentro de la misma fuente y solo se registra un cambio si HAY diferencia. Emite 'Cambio de estructura detectado'.");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s1}/comparaciones", null, "ti");

        Paso("Un usuario sin permiso intenta registrar una fuente",
            "C7 (simulado) deniega la operacion antes de tocar el dominio y deja registro (contrato 4.12).");
        await Llamar(_c1, HttpMethod.Post, "/v1/fuentes",
            new { nombre = "Intento no autorizado", tipo = "MotorRelacional", credenciales = (string?)null }, "consulta");
        await Llamar(_c1, HttpMethod.Get, "/v1/simulacion/denegaciones-c7");

        Paso("El CRM (S2) depende de su proveedor",
            "Registramos S2 y la autorizamos. Al importar, el proveedor aun no ha entregado la estructura: la fuente 'no esta disponible'.");
        var s2 = (await Llamar(_c1, HttpMethod.Post, "/v1/fuentes",
            new { nombre = "S2 CRM", tipo = "SistemaDeProveedor", credenciales = (string?)null }, "admin")).Texto("fuenteId");
        if (s2 is null) return;
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s2}/autorizaciones", new { autorizador = "Jefa de administracion" }, "admin");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s2}/importaciones", null, "ti");

        Paso("El proveedor entrega la estructura y se reintenta",
            "Simulamos la entrega del proveedor. El reintento es manual (decision #7 del registro de cambios).");
        await Llamar(_c1, HttpMethod.Put, $"/v1/simulacion/fuentes/{s2}/estructura", Estructuras.DelProveedorCrm());
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{s2}/importaciones", null, "ti");

        Paso("Ver lo que C1 publico",
            "Los eventos de dominio salen traducidos a contratos V1 (capa anticorrupcion). C6 y C2 solo reciben los que les interesan.");
        _presentador.MostrarMensajes(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/mensajes-publicados"));

        _consola.Escribir("");
        _consola.Escribir("=== Fin de la demostracion de C1 ===");
    }

    // =====================================================================================================
    // Demostracion 2: C1 -> C2 (interoperabilidad real por HTTP)
    // =====================================================================================================
    public async Task DeC1ConC2()
    {
        Titulo("Demostracion C1 -> C2: la fuente nace en C1 y C2 la conoce por el evento 'Fuente registrada'");
        _consola.Escribir("    Requisitos: C2 levantado en su puerto y C1 arrancado con la variable C2_URL apuntando a C2.");

        Paso("Salud de C1 y de C2", "Ambos servicios deben responder.");
        var saludC1 = await Llamar(_c1, HttpMethod.Get, "/salud");
        var saludC2 = await Llamar(_c2, HttpMethod.Get, "/salud");
        if (!saludC1.Exitosa || !saludC2.Exitosa) return;

        Paso("C2 rechaza un atributo de una fuente que NO existe en C1",
            "Regla V11 de C2: 'un atributo solo se carga si su fuente existe en C1'. Usamos un id inventado.");
        await Llamar(_c2, HttpMethod.Post, "/v1/atributos",
            new { nombre = "cedula", fuente_id = Guid.NewGuid().ToString(), contiene_datos_personales = true });

        Paso("C1 registra la fuente",
            "C1 guarda la fuente y publica 'FuenteRegistrada.v1' por HTTP a C2 (relacion Open Host Service / Published Language; C2 es conformista).");
        var fuente = (await Llamar(_c1, HttpMethod.Post, "/v1/fuentes",
            new { nombre = "S1 Contabilidad (demo con C2)", tipo = "MotorRelacional", credenciales = "usuario=ro;clave=123" }, "admin")).Texto("fuenteId");
        if (fuente is null) return;

        var simulados = await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/c2/recibidos");
        if (simulados.Json() is { ValueKind: JsonValueKind.Array } lista && lista.GetArrayLength() > 0)
        {
            _consola.Escribir("  [!] C1 esta usando un C2 SIMULADO en memoria: no se arranco con C2_URL.");
            _consola.Escribir("      Cierre C1 y vuelva a arrancarlo con:  $env:C2_URL = \"http://localhost:8012\"");
            _consola.Escribir("      El siguiente paso va a fallar hasta que lo haga.");
        }

        Paso("Revisar si hubo entregas fallidas en C1",
            "Si C2 estuviera apagado, C1 igual guarda la fuente (decision #11: a lo sumo una vez, sin Outbox) y anota la entrega fallida.");
        await Llamar(_c1, HttpMethod.Get, "/v1/simulacion/entregas-fallidas");

        Paso("C2 ahora acepta el atributo",
            "Con el mismo cuerpo de antes, pero con el id REAL de la fuente. C2 ya conoce la fuente porque recibio el evento.");
        await Llamar(_c2, HttpMethod.Post, "/v1/atributos",
            new { nombre = "cedula", fuente_id = fuente, contiene_datos_personales = true });

        Paso("Consultar en C2 los atributos de esa fuente", "Lectura en C2 filtrada por la fuente que nacio en C1.");
        await Llamar(_c2, HttpMethod.Get, $"/v1/atributos?fuente_id={Uri.EscapeDataString(fuente)}");

        Paso("C1 importa metadatos y avisa a C6 (simulado)",
            "Autorizamos, definimos la estructura e importamos. C1 publica 'MetadatosTecnicosImportados.v1' y C6 (simulado) lo recibe.");
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{fuente}/autorizaciones", new { autorizador = "Jefa de administracion" }, "admin");
        await Llamar(_c1, HttpMethod.Put, $"/v1/simulacion/fuentes/{fuente}/estructura", Estructuras.Base());
        await Llamar(_c1, HttpMethod.Post, $"/v1/fuentes/{fuente}/importaciones", null, "ti");
        _presentador.MostrarMensajes(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/c6/recibidos"));

        _consola.Escribir("");
        _consola.Escribir("=== Fin de la demostracion C1 -> C2 ===");
    }
}