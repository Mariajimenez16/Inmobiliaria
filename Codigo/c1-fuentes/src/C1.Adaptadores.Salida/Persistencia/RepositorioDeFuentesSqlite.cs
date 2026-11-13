using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IRepositorioDeFuentes. Guarda la raiz Fuente junto con sus autorizaciones (parte del agregado).
// Usa Fuente.Reconstituir para reconstruirla SIN pasar por las reglas ni levantar eventos.
internal sealed class RepositorioDeFuentesSqlite : IRepositorioDeFuentes
{
    private readonly ContextoSqlite _ctx;

    public RepositorioDeFuentesSqlite(ContextoSqlite ctx) => _ctx = ctx;

    public async Task<Fuente?> Obtener(FuenteId id, CancellationToken ct)
    {
        string nombre;
        TipoDeFuente tipo;
        DateTimeOffset registradaEn;
        DateTimeOffset? ultimaImportacion;

        await using (var cmd = await _ctx.Comando(
            "SELECT nombre, tipo, registrada_en, ultima_importacion_en FROM fuentes WHERE id = $id", ct))
        {
            cmd.Param("$id", id.ToString());
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            if (!await lector.ReadAsync(ct))
                return null;

            nombre = lector.GetString(0);
            tipo = Enum.Parse<TipoDeFuente>(lector.GetString(1));
            registradaEn = Fechas.Leer(lector.GetString(2));
            ultimaImportacion = lector.IsDBNull(3) ? (DateTimeOffset?)null : Fechas.Leer(lector.GetString(3));
        }

        var autorizaciones = await CargarAutorizaciones(id, ct);
        return Fuente.Reconstituir(id, nombre, tipo, registradaEn, ultimaImportacion, autorizaciones);
    }

    public async Task<IReadOnlyList<Fuente>> Listar(CancellationToken ct)
    {
        var ids = new List<FuenteId>();
        await using (var cmd = await _ctx.Comando("SELECT id FROM fuentes ORDER BY registrada_en", ct))
        await using (var lector = await cmd.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                ids.Add(FuenteId.Desde(lector.GetString(0)));
        }

        var fuentes = new List<Fuente>();
        foreach (var id in ids)
        {
            var fuente = await Obtener(id, ct);
            if (fuente is not null)
                fuentes.Add(fuente);
        }
        return fuentes;
    }

    public async Task Agregar(Fuente fuente, CancellationToken ct)
    {
        await using (var cmd = await _ctx.Comando(
            "INSERT INTO fuentes (id, nombre, tipo, registrada_en, ultima_importacion_en) VALUES ($id, $nombre, $tipo, $reg, $ult)", ct))
        {
            cmd.Param("$id", fuente.Id.ToString());
            cmd.Param("$nombre", fuente.Nombre);
            cmd.Param("$tipo", fuente.Tipo.ToString());
            cmd.Param("$reg", Fechas.Escribir(fuente.RegistradaEn));
            cmd.Param("$ult", Fechas.Escribir(fuente.UltimaImportacionEn));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await GuardarAutorizaciones(fuente, ct);
    }

    public async Task Actualizar(Fuente fuente, CancellationToken ct)
    {
        int filas;
        await using (var cmd = await _ctx.Comando(
            "UPDATE fuentes SET ultima_importacion_en = $ult WHERE id = $id", ct))
        {
            cmd.Param("$ult", Fechas.Escribir(fuente.UltimaImportacionEn));
            cmd.Param("$id", fuente.Id.ToString());
            filas = await cmd.ExecuteNonQueryAsync(ct);
        }

        if (filas == 0)
            throw new ErrorNoEncontrado($"No existe la fuente '{fuente.Id}'.");

        await GuardarAutorizaciones(fuente, ct);
    }

    private async Task GuardarAutorizaciones(Fuente fuente, CancellationToken ct)
    {
        foreach (var a in fuente.Autorizaciones)
        {
            await using var cmd = await _ctx.Comando(
                @"INSERT INTO autorizaciones (id, fuente_id, autorizador, autorizada_en, revocada_en)
                  VALUES ($id, $fuente, $autorizador, $en, $rev)
                  ON CONFLICT(id) DO UPDATE SET revocada_en = excluded.revocada_en", ct);
            cmd.Param("$id", a.Id.ToString());
            cmd.Param("$fuente", fuente.Id.ToString());
            cmd.Param("$autorizador", a.Autorizador);
            cmd.Param("$en", Fechas.Escribir(a.AutorizadaEn));
            cmd.Param("$rev", Fechas.Escribir(a.RevocadaEn));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<List<AutorizacionDeAcceso>> CargarAutorizaciones(FuenteId id, CancellationToken ct)
    {
        var lista = new List<AutorizacionDeAcceso>();
        await using var cmd = await _ctx.Comando(
            "SELECT id, autorizador, autorizada_en, revocada_en FROM autorizaciones WHERE fuente_id = $f ORDER BY autorizada_en", ct);
        cmd.Param("$f", id.ToString());
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            lista.Add(AutorizacionDeAcceso.Reconstituir(
                Guid.Parse(lector.GetString(0)),
                id,
                lector.GetString(1),
                Fechas.Leer(lector.GetString(2)),
                lector.IsDBNull(3) ? (DateTimeOffset?)null : Fechas.Leer(lector.GetString(3))));
        }
        return lista;
    }
}