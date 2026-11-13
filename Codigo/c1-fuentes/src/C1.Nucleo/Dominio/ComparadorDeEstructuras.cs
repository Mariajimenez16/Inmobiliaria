namespace C1.Nucleo.Dominio;

// Servicio de dominio: logica pura que no pertenece a una sola entidad (compara DOS versiones).
// SOLID - SRP: solo compara. No guarda, no publica, no conoce la fuente.
// Una columna "modificada" es la que cambia de tipo, de longitud o de obligatoriedad.
// Un renombrado no se puede distinguir de "eliminada + agregada" solo comparando estructuras (ver decisiones).
public static class ComparadorDeEstructuras
{
    public static IReadOnlyList<DiferenciaDeEstructura> Comparar(VersionDeEstructura anterior, VersionDeEstructura nueva)
    {
        var antes = anterior.Columnas.ToDictionary(c => c.Clave, StringComparer.OrdinalIgnoreCase);
        var despues = nueva.Columnas.ToDictionary(c => c.Clave, StringComparer.OrdinalIgnoreCase);
        var resultado = new List<DiferenciaDeEstructura>();

        foreach (var (clave, columna) in despues)
        {
            if (!antes.ContainsKey(clave))
                resultado.Add(new DiferenciaDeEstructura(
                    TipoDeDiferencia.ColumnaAgregada, columna.Tabla, columna.Columna,
                    $"Nueva columna de tipo {columna.TipoDeDato}."));
        }

        foreach (var (clave, previa) in antes)
        {
            if (!despues.TryGetValue(clave, out var actual))
            {
                resultado.Add(new DiferenciaDeEstructura(
                    TipoDeDiferencia.ColumnaEliminada, previa.Tabla, previa.Columna,
                    "La columna ya no existe en la nueva version."));
            }
            else
            {
                var detalle = DescribirCambios(previa, actual);
                if (detalle.Length > 0)
                    resultado.Add(new DiferenciaDeEstructura(
                        TipoDeDiferencia.ColumnaModificada, actual.Tabla, actual.Columna, detalle));
            }
        }

        return resultado
            .OrderBy(d => d.Tabla, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Columna, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DescribirCambios(MetadatoTecnico previa, MetadatoTecnico actual)
    {
        var cambios = new List<string>();
        if (!string.Equals(previa.TipoDeDato, actual.TipoDeDato, StringComparison.OrdinalIgnoreCase))
            cambios.Add($"tipo {previa.TipoDeDato} -> {actual.TipoDeDato}");
        if (previa.Longitud != actual.Longitud)
            cambios.Add($"longitud {previa.Longitud?.ToString() ?? "sin dato"} -> {actual.Longitud?.ToString() ?? "sin dato"}");
        if (previa.Obligatorio != actual.Obligatorio)
            cambios.Add($"obligatoria {previa.Obligatorio} -> {actual.Obligatorio}");
        return string.Join("; ", cambios);
    }
}