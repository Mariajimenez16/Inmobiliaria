using System.Globalization;
using Microsoft.Data.Sqlite;

namespace C1.Adaptadores.Salida.Persistencia;

// Una conexion + una transaccion por "unidad de trabajo". Todos los repositorios la comparten, asi lo que se escribe
// en varias tablas se confirma junto (Confirmar) o se descarta junto (si se libera sin confirmar, se hace rollback).
internal sealed class ContextoSqlite : IDisposable
{
    private readonly string _cadena;
    private SqliteConnection? _conexion;
    private SqliteTransaction? _transaccion;

    public ContextoSqlite(string cadenaDeConexion) => _cadena = cadenaDeConexion;

    public async Task<SqliteCommand> Comando(string sql, CancellationToken ct)
    {
        if (_conexion is null)
        {
            _conexion = new SqliteConnection(_cadena);
            await _conexion.OpenAsync(ct);
            using var pragma = _conexion.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(ct);
        }

        _transaccion ??= _conexion.BeginTransaction();
        var comando = _conexion.CreateCommand();
        comando.CommandText = sql;
        comando.Transaction = _transaccion;
        return comando;
    }

    public async Task Confirmar(CancellationToken ct)
    {
        if (_transaccion is null) return;
        await _transaccion.CommitAsync(ct);
        await _transaccion.DisposeAsync();
        _transaccion = null;
    }

    public void Dispose()
    {
        _transaccion?.Dispose(); // sin Confirmar => rollback
        _conexion?.Dispose();
    }
}

internal static class Fechas
{
    public static string Escribir(DateTimeOffset fecha) => fecha.ToString("o", CultureInfo.InvariantCulture);

    public static string? Escribir(DateTimeOffset? fecha) => fecha is null ? null : Escribir(fecha.Value);

    public static DateTimeOffset Leer(string texto) => DateTimeOffset.ParseExact(texto, "o", CultureInfo.InvariantCulture);
}

internal static class Sql
{
    public static void Param(this SqliteCommand comando, string nombre, object? valor) =>
        comando.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
}