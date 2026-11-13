namespace C1.Nucleo.Dominio;

public enum TipoDeDiferencia
{
    ColumnaAgregada,
    ColumnaEliminada,
    ColumnaModificada
}

// Objeto de valor: una diferencia concreta detectada entre dos versiones.
public sealed record DiferenciaDeEstructura(TipoDeDiferencia Tipo, string Tabla, string Columna, string Detalle);