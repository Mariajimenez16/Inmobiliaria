using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;
using Microsoft.Data.Sqlite;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IRepositorioDeVersiones. Las versiones viven FUERA del agregado Fuente (V6.a): tienen su propia tabla.
// La restriccion UNIQUE(fuente_id, numero) impide que dos importaciones simultaneas creen la misma version.
internal sealed class RepositorioDeVersionesSqlite : IRepositorioDeVersiones
{
    private readonly ContextoSqlite _ctx;

    public RepositorioDeVersionesSqlite(ContextoSqlite ctx) => _ctx = ctx;

    public async Task<VersionDeEstructura?> ObtenerUltima(FuenteId fuenteId, CancellationToken ct)
    {
        var lista = await Leer(
            "SELECT id, fuente_id, numero, importada_en FROM versiones WHERE fuente_id = $f ORDER BY numero DESC LIMIT 1",
            c => c.Param("$f", fuenteId.ToString()), ct);
        return lista.FirstOrDefault();
    }

    public async Task<VersionDeEstructura?> Obtener(FuenteId fuenteId, int numero, CancellationToken ct)
    {
        var lista = await Leer(
            "SELECT id, fuente_id, numero, importada_en FROM versiones WHERE fuente_id = $f AND numero = $n",
            c =>
            {
                c.Param("$f", fuenteId.ToString());
                c.Param("$n", numero);
            }, ct);
        return lista.FirstOrDefault();
    }

    public async Task<IReadOnlyList<VersionDeEstructura>> Listar(FuenteId fuenteId, CancellationToken ct) =>
        await Leer(
            "SELECT id, fuente_id, numero, importada_en FROM versiones WHERE fuente_id = $f ORDER BY numero",
            c => c.Param("$f", fuenteId.ToString()), ct);

    public async Task Agregar(VersionDeEstructura version, CancellationToken ct)
    {
        try
        {
            await using (var cmd = await _ctx.Comando(
                "INSERT INTO versiones (id, fuente_id, numero, importada_en) VALUES ($id, $f, $n, $en)", ct))
            {
                cmd.Param("$id", version.Id.ToString());
                cmd.Param("$f", version.FuenteId.ToString());
                cmd.Param("$n", version.Numero);
                cmd.Param("$en", Fechas.Escribir(version.ImportadaEn));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // violacion de restriccion UNIQUE
        {
            throw new ErrorDeConflicto(
                $"Otra importacion ya registro la version {version.Numero} de esta fuente. Intente de nuevo.");
        }

        foreach (var c in version.Columnas)
        {
            await using var cmd = await _ctx.Comando(
                @"INSERT INTO columnas_version (version_id, tabla, columna, tipo_de_dato, longitud, obligatorio)
                  VALUES ($v, $t, $c, $tipo, $l, $o)", ct);
            cmd.Param("$v", version.Id.ToString());
            cmd.Param("$t", c.Tabla);
            cmd.Param("$c", c.Columna);
            cmd.Param("$tipo", c.TipoDeDato);
            cmd.Param("$l", c.Longitud);
            cmd.Param("$o", c.Obligatorio ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<List<VersionDeEstructura>> Leer(string sql, Action<SqliteCommand> parametros, CancellationToken ct)
    {
        var filas = new List<(Guid Id, FuenteId FuenteId, int Numero, DateTimeOffset ImportadaEn)>();
        await using (var cmd = await _ctx.Comando(sql, ct))
        {
            parametros(cmd);
            await using var lector = await cmd.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                filas.Add((
                    Guid.Parse(lector.GetString(0)),
                    FuenteId.Desde(lector.GetString(1)),
                    lector.GetInt32(2),
                    Fechas.Leer(lector.GetString(3))));
            }
        }

        var resultado = new List<VersionDeEstructura>();
        foreach (var fila in filas)
        {
            var columnas = await CargarColumnas(fila.Id, ct);
            resultado.Add(VersionDeEstructura.Reconstituir(fila.Id, fila.FuenteId, fila.Numero, fila.ImportadaEn, columnas));
        }
        return resultado;
    }

    private async Task<List<MetadatoTecnico>> CargarColumnas(Guid versionId, CancellationToken ct)
    {
        var columnas = new List<MetadatoTecnico>();
        await using var cmd = await _ctx.Comando(
            "SELECT tabla, columna, tipo_de_dato, longitud, obligatorio FROM columnas_version WHERE version_id = $v ORDER BY rowid", ct);
        cmd.Param("$v", versionId.ToString());
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            columnas.Add(new MetadatoTecnico(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetString(2),
                lector.IsDBNull(3) ? (int?)null : lector.GetInt32(3),
                lector.GetInt32(4) == 1));
        }
        return columnas;
    }
}