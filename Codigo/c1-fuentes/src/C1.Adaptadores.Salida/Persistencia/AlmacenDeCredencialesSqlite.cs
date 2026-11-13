using C1.Adaptadores.Salida.Cifrado;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Persistencia;

// Adaptador del puerto IAlmacenDeCredenciales. La cuenta de solo lectura de una fuente NUNCA se guarda en claro:
// el nucleo entrega el texto, y este adaptador lo cifra antes de escribirlo y lo descifra al leerlo.
// SOLID - SRP: guarda y recupera credenciales; el cifrado lo hace ICifrador (DIP).
internal sealed class AlmacenDeCredencialesSqlite : IAlmacenDeCredenciales
{
    private readonly ContextoSqlite _ctx;
    private readonly ICifrador _cifrador;

    public AlmacenDeCredencialesSqlite(ContextoSqlite ctx, ICifrador cifrador)
    {
        _ctx = ctx;
        _cifrador = cifrador;
    }

    public async Task Guardar(FuenteId fuenteId, string credenciales, CancellationToken ct)
    {
        await using var cmd = await _ctx.Comando(
            @"INSERT INTO credenciales (fuente_id, credenciales_cifradas) VALUES ($f, $c)
              ON CONFLICT(fuente_id) DO UPDATE SET credenciales_cifradas = excluded.credenciales_cifradas", ct);
        cmd.Param("$f", fuenteId.ToString());
        cmd.Param("$c", _cifrador.Cifrar(credenciales));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> Obtener(FuenteId fuenteId, CancellationToken ct)
    {
        await using var cmd = await _ctx.Comando(
            "SELECT credenciales_cifradas FROM credenciales WHERE fuente_id = $f", ct);
        cmd.Param("$f", fuenteId.ToString());
        var valor = await cmd.ExecuteScalarAsync(ct);
        return valor is string cifrado ? _cifrador.Descifrar(cifrado) : null;
    }
}