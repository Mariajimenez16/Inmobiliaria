using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;
using Microsoft.Data.Sqlite;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IRepositorioDeCambios. El cambio y sus diferencias son historial tecnico, fuera del agregado Fuente.
// La revision (quien y cuando) se guarda en su propia tabla: asi no hay que migrar la tabla 'cambios' de bases ya creadas.
internal sealed class RepositorioDeCambiosSqlite : IRepositorioDeCambios
{
    private readonly ContextoSqlite _ctx;

    public RepositorioDeCambiosSqlite(ContextoSqlite ctx) => _ctx = ctx;

    public async Task Agregar(CambioDeEstructura cambio, CancellationToken ct)
    {
        await using (var cmd = await _ctx.Comando(
            @"INSERT INTO cambios (id, fuente_id, version_anterior, version_nueva, detectado_en)
              VALUES ($id, $f, $va, $vn, $en)", ct))
        {
            cmd.Param("$id", cambio.Id.ToString());
            cmd.Param("$f", cambio.FuenteId.ToString());
            cmd.Param("$va", cambio.VersionAnterior);
            cmd.Param("$vn", cambio.VersionNueva);
            cmd.Param("$en", Fechas.Escribir(cambio.DetectadoEn));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var d in cambio.Diferencias)
        {
            await using var cmd = await _ctx.Comando(
                "INSERT INTO diferencias (cambio_id, tipo, tabla, columna, detalle) VALUES ($c, $tipo, $t, $col, $d)", ct);
            cmd.Param("$c", cambio.Id.ToString());
            cmd.Param("$tipo", d.Tipo.ToString());
            cmd.Param("$t", d.Tabla);
            cmd.Param("$col", d.Columna);
            cmd.Param("$d", d.Detalle);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // Lo unico que cambia despues de crear un cambio es su revision.
    public async Task Actualizar(CambioDeEstructura cambio, CancellationToken ct)
    {
        if (cambio.RevisadoEn is null)
            return;

        await using var cmd = await _ctx.Comando(
            @"INSERT INTO revisiones_cambio (cambio_id, revisado_por, revisado_en) VALUES ($c, $p, $e)
              ON CONFLICT(cambio_id) DO UPDATE SET revisado_por = excluded.revisado_por, revisado_en = excluded.revisado_en", ct);
        cmd.Param("$c", cambio.Id.ToString());
        cmd.Param("$p", cambio.RevisadoPor);
        cmd.Param("$e", Fechas.Escribir(cambio.RevisadoEn));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CambioDeEstructura?> Obtener(Guid cambioId, CancellationToken ct)
    {
        var encontrados = await Consultar("WHERE c.id = $id", cmd => cmd.Param("$id", cambioId.ToString()), ct);
        return encontrados.FirstOrDefault();
    }

    public Task<IReadOnlyList<CambioDeEstructura>> ListarPorFuente(FuenteId fuenteId, CancellationToken ct) =>
        Consultar("WHERE c.fuente_id = $f", cmd => cmd.Param("$f", fuenteId.ToString()), ct);

    private async Task<IReadOnlyList<CambioDeEstructura>> Consultar(
        string filtro, Action<SqliteCommand> parametros, CancellationToken ct)
    {
        var filas = new List<(Guid Id, FuenteId Fuente, int Anterior, int Nueva, DateTimeOffset DetectadoEn, string? Por, DateTimeOffset? En)>();
        await using (var cmd = await _ctx.Comando(
            $@"SELECT c.id, c.fuente_id, c.version_anterior, c.version_nueva, c.detectado_en, r.revisado_por, r.revisado_en
               FROM cambios c LEFT JOIN revisiones_cambio r ON r.cambio_id = c.id
               {filtro} ORDER BY c.detectado_en", ct))
        {
            parametros(cmd);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                filas.Add((
                    Guid.Parse(lector.GetString(0)),
                    FuenteId.Desde(lector.GetString(1)),
                    lector.GetInt32(2),
                    lector.GetInt32(3),
                    Fechas.Leer(lector.GetString(4)),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? (DateTimeOffset?)null : Fechas.Leer(lector.GetString(6))));
            }
        }

        var resultado = new List<CambioDeEstructura>();
        foreach (var fila in filas)
        {
            var diferencias = await CargarDiferencias(fila.Id, ct);
            resultado.Add(CambioDeEstructura.Reconstituir(
                fila.Id, fila.Fuente, fila.Anterior, fila.Nueva, fila.DetectadoEn, diferencias, fila.Por, fila.En));
        }
        return resultado;
    }

    private async Task<List<DiferenciaDeEstructura>> CargarDiferencias(Guid cambioId, CancellationToken ct)
    {
        var lista = new List<DiferenciaDeEstructura>();
        await using var cmd = await _ctx.Comando(
            "SELECT tipo, tabla, columna, detalle FROM diferencias WHERE cambio_id = $c ORDER BY rowid", ct);
        cmd.Param("$c", cambioId.ToString());
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            lista.Add(new DiferenciaDeEstructura(
                Enum.Parse<TipoDeDiferencia>(lector.GetString(0)),
                lector.GetString(1),
                lector.GetString(2),
                lector.GetString(3)));
        }
        return lista;
    }
}