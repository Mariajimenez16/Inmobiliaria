using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IRepositorioDeAvisos (SOLID - DIP: el nucleo solo conoce la interfaz).
internal sealed class RepositorioDeAvisosSqlite : IRepositorioDeAvisos
{
    private readonly ContextoSqlite _ctx;

    public RepositorioDeAvisosSqlite(ContextoSqlite ctx) => _ctx = ctx;

    public async Task Agregar(AvisoDeCambio aviso, CancellationToken ct)
    {
        await using var cmd = await _ctx.Comando(
            @"INSERT INTO avisos (id, proveedor, descripcion, fuente_id, recibido_en)
              VALUES ($id, $p, $d, $f, $r)", ct);
        cmd.Param("$id", aviso.Id.ToString());
        cmd.Param("$p", aviso.Proveedor);
        cmd.Param("$d", aviso.Descripcion);
        cmd.Param("$f", aviso.FuenteId?.ToString());
        cmd.Param("$r", Fechas.Escribir(aviso.RecibidoEn));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AvisoDeCambio>> Listar(FuenteId? fuenteId, CancellationToken ct)
    {
        var sql = "SELECT id, proveedor, descripcion, fuente_id, recibido_en FROM avisos"
                  + (fuenteId is null ? "" : " WHERE fuente_id = $f")
                  + " ORDER BY recibido_en";

        var lista = new List<AvisoDeCambio>();
        await using var cmd = await _ctx.Comando(sql, ct);
        if (fuenteId is FuenteId filtro)
            cmd.Param("$f", filtro.ToString());

        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            lista.Add(AvisoDeCambio.Reconstituir(
                Guid.Parse(lector.GetString(0)),
                lector.GetString(1),
                lector.GetString(2),
                lector.IsDBNull(3) ? (FuenteId?)null : FuenteId.Desde(lector.GetString(3)),
                Fechas.Leer(lector.GetString(4))));
        }
        return lista;
    }
}