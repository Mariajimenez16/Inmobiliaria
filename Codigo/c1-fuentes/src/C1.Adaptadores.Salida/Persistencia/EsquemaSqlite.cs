using Microsoft.Data.Sqlite;

namespace C1.Adaptadores.Salida.Persistencia;

public sealed record OpcionesSqlite(string RutaDelArchivo)
{
    public string CadenaDeConexion => new SqliteConnectionStringBuilder { DataSource = RutaDelArchivo }.ToString();
}

// Crea las tablas si no existen. Se llama una vez al arrancar la aplicacion.
public static class EsquemaSqlite
{
    private const string Script = @"
CREATE TABLE IF NOT EXISTS fuentes (
    id TEXT PRIMARY KEY,
    nombre TEXT NOT NULL,
    tipo TEXT NOT NULL,
    registrada_en TEXT NOT NULL,
    ultima_importacion_en TEXT NULL
);
CREATE TABLE IF NOT EXISTS autorizaciones (
    id TEXT PRIMARY KEY,
    fuente_id TEXT NOT NULL REFERENCES fuentes(id),
    autorizador TEXT NOT NULL,
    autorizada_en TEXT NOT NULL,
    revocada_en TEXT NULL
);
CREATE TABLE IF NOT EXISTS versiones (
    id TEXT PRIMARY KEY,
    fuente_id TEXT NOT NULL,
    numero INTEGER NOT NULL,
    importada_en TEXT NOT NULL,
    UNIQUE (fuente_id, numero)
);
CREATE TABLE IF NOT EXISTS columnas_version (
    version_id TEXT NOT NULL,
    tabla TEXT NOT NULL,
    columna TEXT NOT NULL,
    tipo_de_dato TEXT NOT NULL,
    longitud INTEGER NULL,
    obligatorio INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS cambios (
    id TEXT PRIMARY KEY,
    fuente_id TEXT NOT NULL,
    version_anterior INTEGER NOT NULL,
    version_nueva INTEGER NOT NULL,
    detectado_en TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS diferencias (
    cambio_id TEXT NOT NULL,
    tipo TEXT NOT NULL,
    tabla TEXT NOT NULL,
    columna TEXT NOT NULL,
    detalle TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS credenciales (
    fuente_id TEXT PRIMARY KEY,
    credenciales_cifradas TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS revisiones_cambio (
    cambio_id TEXT PRIMARY KEY,
    revisado_por TEXT NOT NULL,
    revisado_en TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS avisos (
    id TEXT PRIMARY KEY,
    proveedor TEXT NOT NULL,
    descripcion TEXT NOT NULL,
    fuente_id TEXT NULL,
    recibido_en TEXT NOT NULL
);";

    public static void Inicializar(OpcionesSqlite opciones)
    {
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(opciones.RutaDelArchivo));
        if (!string.IsNullOrEmpty(carpeta))
            Directory.CreateDirectory(carpeta);

        using var conexion = new SqliteConnection(opciones.CadenaDeConexion);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = Script;
        comando.ExecuteNonQuery();
    }
}