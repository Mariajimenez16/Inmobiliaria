namespace C1.Cliente;

public sealed record ColumnaDeEstructura(string Tabla, string Columna, string TipoDeDato, int? Longitud, bool Obligatorio);

// Estructuras de ejemplo para la simulacion: lo que una fuente "tiene" y como "cambia".
public static class Estructuras
{
    public static object Base() => Armar(new[]
    {
        new ColumnaDeEstructura("clientes", "cedula", "varchar", 20, true),
        new ColumnaDeEstructura("clientes", "nombre", "varchar", 100, true),
        new ColumnaDeEstructura("contratos", "vr_can", "decimal", null, true),
        new ColumnaDeEstructura("contratos", "estado", "varchar", 20, false)
    });

    // Cambia el tipo de cedula, elimina contratos.estado y agrega clientes.correo.
    public static object Modificada() => Armar(new[]
    {
        new ColumnaDeEstructura("clientes", "cedula", "bigint", null, true),
        new ColumnaDeEstructura("clientes", "nombre", "varchar", 100, true),
        new ColumnaDeEstructura("clientes", "correo", "varchar", 120, false),
        new ColumnaDeEstructura("contratos", "vr_can", "decimal", null, true)
    });

    // Lo que el proveedor del CRM (S2) entregaria.
    public static object DelProveedorCrm() => Armar(new[]
    {
        new ColumnaDeEstructura("interesados", "nombre", "varchar", 100, true),
        new ColumnaDeEstructura("interesados", "telefono", "varchar", 15, false)
    });

    public static object Armar(IEnumerable<ColumnaDeEstructura> columnas) => new { columnas };
}