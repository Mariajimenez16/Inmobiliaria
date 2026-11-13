namespace C1.Cliente;

// Menu de operaciones sueltas sobre C1. Cliente DELGADO: cada opcion pide datos, llama a la API y muestra la respuesta.
// SOLID - SRP: solo interaccion con la persona. Las reglas viven en C1 (y se ven aqui como los codigos HTTP que devuelve).
public sealed class MenuDeC1
{
    private static readonly string[] Tipos = { "MotorRelacional", "SistemaDeProveedor", "NoEstructurada" };

    private readonly ClienteHttp _c1;
    private readonly Presentador _presentador;
    private readonly IConsola _consola;
    private readonly List<(string Texto, Func<Task> Accion)> _acciones;
    private string _usuario = "admin";
    private string? _ultimaFuente;

    public MenuDeC1(ClienteHttp c1, Presentador presentador, IConsola consola, DemostracionGuiada demostraciones)
    {
        _c1 = c1;
        _presentador = presentador;
        _consola = consola;
        _acciones = new List<(string, Func<Task>)>
        {
            ("Cambiar de usuario (admin, ti, consulta, oficial...)", CambiarUsuario),
            ("Registrar fuente", RegistrarFuente),
            ("Autorizar acceso a metadatos", AutorizarAcceso),
            ("Revocar autorizacion", RevocarAutorizacion),
            ("Importar metadatos tecnicos", Importar),
            ("Comparar estructuras", Comparar),
            ("Listar fuentes", ListarFuentes),
            ("Ver una fuente", VerFuente),
            ("Listar versiones de una fuente", ListarVersiones),
            ("Ver una version (con sus columnas)", VerVersion),
            ("Listar cambios de estructura detectados", ListarCambios),
            ("Ver el estado de la importacion de una fuente", VerEstadoDeImportacion),
            ("Reintentar la importacion de metadatos", ReintentarImportacion),
            ("Marcar un cambio de estructura como revisado", MarcarCambioRevisado),
            ("A3: registrar aviso de cambio de un proveedor", RegistrarAviso),
            ("A3: listar avisos de proveedores", ListarAvisos),
            ("SIMULACION: definir la estructura que entrega una fuente", DefinirEstructura),
            ("SIMULACION: ver lo que C1 publico y lo que denego C7", VerSimulacion),
            ("Demostracion guiada de C1", demostraciones.DeC1),
            ("Demostracion guiada C1 -> C2 (requiere C2 levantado)", demostraciones.DeC1ConC2)
        };
    }

    public async Task Ejecutar()
    {
        _consola.Escribir("=== C1 - Fuentes y metadatos tecnicos (Hogar Andino) ===");
        _consola.Escribir($"Servicio C1: {_c1.UrlBase}");

        while (true)
        {
            _consola.Escribir("");
            _consola.Escribir($"Usuario: {_usuario}" + (_ultimaFuente is null ? "" : $"   |   Ultima fuente: {_ultimaFuente}"));
            for (var i = 0; i < _acciones.Count; i++)
                _consola.Escribir($"  {i + 1}. {_acciones[i].Texto}");
            _consola.Escribir("  0. Salir");

            var opcion = _consola.Leer("Opcion: ")?.Trim();
            if (opcion is null || opcion == "0")
                return;
            if (!int.TryParse(opcion, out var numero) || numero < 1 || numero > _acciones.Count)
            {
                _consola.Escribir("Opcion no valida.");
                continue;
            }

            try
            {
                await _acciones[numero - 1].Accion();
            }
            catch (Exception ex)
            {
                _consola.Escribir($"  [X] Error inesperado en el cliente: {ex.Message}");
            }
        }
    }

    // ---------- entrada de datos ----------

    private string Pedir(string etiqueta, string? porDefecto = null)
    {
        var texto = _consola.Leer(porDefecto is null ? $"{etiqueta}: " : $"{etiqueta} [{porDefecto}]: ")?.Trim();
        return string.IsNullOrEmpty(texto) ? porDefecto ?? "" : texto;
    }

    private string? PedirFuente()
    {
        var id = Pedir("Id de la fuente", _ultimaFuente);
        if (string.IsNullOrEmpty(id))
        {
            _consola.Escribir("  Debe indicar el id de una fuente (registre una primero).");
            return null;
        }

        return Uri.EscapeDataString(id);
    }

    private string ElegirTipo()
    {
        for (var i = 0; i < Tipos.Length; i++)
            _consola.Escribir($"    {i + 1}. {Tipos[i]}");
        var texto = Pedir("Tipo de fuente", "1");
        return int.TryParse(texto, out var n) && n >= 1 && n <= Tipos.Length ? Tipos[n - 1] : Tipos[0];
    }

    private static int? ComoEntero(string texto) => int.TryParse(texto, out var n) ? n : null;

    // ---------- operaciones ----------

    private Task CambiarUsuario()
    {
        _usuario = Pedir("Usuario", _usuario);
        return Task.CompletedTask;
    }

    private async Task RegistrarFuente()
    {
        var nombre = Pedir("Nombre de la fuente", "S1 Contabilidad");
        var tipo = ElegirTipo();
        var credenciales = Pedir("Cuenta de solo lectura (vacio = ninguna)", tipo == "MotorRelacional" ? "usuario=ro;clave=123" : "");
        var respuesta = await _c1.Enviar(HttpMethod.Post, "/v1/fuentes",
            new { nombre, tipo, credenciales = string.IsNullOrEmpty(credenciales) ? null : credenciales }, _usuario);
        _presentador.Mostrar(respuesta);
        _ultimaFuente = respuesta.Texto("fuenteId") ?? _ultimaFuente;
    }

    private async Task AutorizarAcceso()
    {
        if (PedirFuente() is not { } id) return;
        var autorizador = Pedir("Quien autoriza", "Jefa de administracion");
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Post, $"/v1/fuentes/{id}/autorizaciones", new { autorizador }, _usuario));
    }

    private async Task RevocarAutorizacion()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Delete, $"/v1/fuentes/{id}/autorizacion", null, _usuario));
    }

    private async Task Importar()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Post, $"/v1/fuentes/{id}/importaciones", null, _usuario));
    }

    private async Task Comparar()
    {
        if (PedirFuente() is not { } id) return;
        var anterior = ComoEntero(Pedir("Version anterior (vacio = comparar las dos mas recientes)"));
        var nueva = ComoEntero(Pedir("Version nueva (vacio = la mas reciente)"));
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Post, $"/v1/fuentes/{id}/comparaciones",
            new { versionAnterior = anterior, versionNueva = nueva }, _usuario));
    }

    private async Task ListarFuentes()
    {
        var tipo = Pedir("Filtrar por tipo (vacio = todas)");
        var ruta = string.IsNullOrEmpty(tipo) ? "/v1/fuentes" : $"/v1/fuentes?tipo={Uri.EscapeDataString(tipo)}";
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, ruta, null, _usuario));
    }

    private async Task VerFuente()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, $"/v1/fuentes/{id}", null, _usuario));
    }

    private async Task ListarVersiones()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, $"/v1/fuentes/{id}/versiones", null, _usuario));
    }

    private async Task VerVersion()
    {
        if (PedirFuente() is not { } id) return;
        var numero = ComoEntero(Pedir("Numero de version", "1")) ?? 1;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, $"/v1/fuentes/{id}/versiones/{numero}", null, _usuario));
    }

    private async Task ListarCambios()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, $"/v1/fuentes/{id}/cambios", null, _usuario));
    }

        private async Task VerEstadoDeImportacion()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, $"/v1/fuentes/{id}/estado-importacion", null, _usuario));
    }

    private async Task ReintentarImportacion()
    {
        if (PedirFuente() is not { } id) return;
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Post, $"/v1/fuentes/{id}/reintentos-de-importacion", null, _usuario));
    }

    private async Task MarcarCambioRevisado()
    {
        if (PedirFuente() is not { } id) return;
        var cambioId = Pedir("Id del cambio (se ve en 'Listar cambios de estructura detectados')");
        if (string.IsNullOrEmpty(cambioId))
        {
            _consola.Escribir("  Debe indicar el id del cambio.");
            return;
        }

        _presentador.Mostrar(await _c1.Enviar(
            HttpMethod.Put, $"/v1/fuentes/{id}/cambios/{Uri.EscapeDataString(cambioId)}/revision", null, _usuario));
    }

    // Adaptador A3: el aviso lo envia el proveedor, no una persona, por eso no se manda usuario.
    private async Task RegistrarAviso()
    {
        var proveedor = Pedir("Proveedor", "Proveedor CRM");
        var descripcion = Pedir("Que cambio", "Cambio en los campos del cliente");
        var fuente = Pedir("Id de la fuente afectada (vacio = queda pendiente de clasificar)");
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Post, "/v1/avisos-de-cambio",
            new { proveedor, descripcion, fuenteId = string.IsNullOrEmpty(fuente) ? null : fuente }));
    }

    private async Task ListarAvisos()
    {
        var fuente = Pedir("Filtrar por fuente (vacio = todos)");
        var ruta = string.IsNullOrEmpty(fuente) ? "/v1/avisos-de-cambio" : $"/v1/avisos-de-cambio?fuenteId={Uri.EscapeDataString(fuente)}";
        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, ruta));
    }

    // ---------- simulacion ----------

    private async Task DefinirEstructura()
    {
        if (PedirFuente() is not { } id) return;
        _consola.Escribir("    1. Estructura base (clientes y contratos)");
        _consola.Escribir("    2. Estructura modificada (cambia un tipo, quita una columna, agrega otra)");
        _consola.Escribir("    3. Estructura que entrega el proveedor del CRM");
        _consola.Escribir("    4. Escribir las columnas a mano");
        var eleccion = Pedir("Elija", "1");

        object estructura = eleccion switch
        {
            "2" => Estructuras.Modificada(),
            "3" => Estructuras.DelProveedorCrm(),
            "4" => Estructuras.Armar(PedirColumnas()),
            _ => Estructuras.Base()
        };

        _presentador.Mostrar(await _c1.Enviar(HttpMethod.Put, $"/v1/simulacion/fuentes/{id}/estructura", estructura));
    }

    private List<ColumnaDeEstructura> PedirColumnas()
    {
        var columnas = new List<ColumnaDeEstructura>();
        _consola.Escribir("    Escriba una columna por linea como: tabla,columna,tipo   (linea vacia para terminar)");
        while (true)
        {
            var linea = _consola.Leer("    > ")?.Trim();
            if (string.IsNullOrEmpty(linea))
                return columnas;
            var partes = linea.Split(',', StringSplitOptions.TrimEntries);
            if (partes.Length < 3)
            {
                _consola.Escribir("    Formato: tabla,columna,tipo");
                continue;
            }

            columnas.Add(new ColumnaDeEstructura(partes[0], partes[1], partes[2], null, true));
        }
    }

    private async Task VerSimulacion()
    {
        _consola.Escribir("    1. Mensajes que C1 publico (contratos V1)");
        _consola.Escribir("    2. Entregas fallidas");
        _consola.Escribir("    3. Denegaciones registradas por C7 (simulado)");
        _consola.Escribir("    4. Lo que recibio C6 (simulado)");
        _consola.Escribir("    5. Lo que recibio C2 (solo si C1 corre SIN C2_URL)");
        var eleccion = Pedir("Elija", "1");

        switch (eleccion)
        {
            case "2": _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/entregas-fallidas")); break;
            case "3": _presentador.Mostrar(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/denegaciones-c7")); break;
            case "4": _presentador.MostrarMensajes(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/c6/recibidos")); break;
            case "5": _presentador.MostrarMensajes(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/c2/recibidos")); break;
            default: _presentador.MostrarMensajes(await _c1.Enviar(HttpMethod.Get, "/v1/simulacion/mensajes-publicados")); break;
        }
    }
}