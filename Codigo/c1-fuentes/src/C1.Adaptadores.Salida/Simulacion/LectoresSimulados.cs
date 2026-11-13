using System.Collections.Concurrent;
using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Adaptadores.Salida.Simulacion;

// Aqui "vive" la estructura que las fuentes simuladas entregarian. Permite demostrar la deteccion de cambios:
// se cambia la estructura de una fuente, se vuelve a importar y se comparan las versiones.
public sealed class EstructurasSimuladasDeFuentes
{
    private readonly ConcurrentDictionary<FuenteId, IReadOnlyList<MetadatoTecnico>> _estructuras = new();

    public void Definir(FuenteId fuenteId, IEnumerable<MetadatoTecnico> columnas) =>
        _estructuras[fuenteId] = columnas.ToList();

    public IReadOnlyList<MetadatoTecnico>? Obtener(FuenteId fuenteId) =>
        _estructuras.TryGetValue(fuenteId, out var columnas) ? columnas : null;
}

// SIMULA el adaptador A1 (V4d): lee el catalogo de un motor relacional (S1, S3, S4) con una cuenta propia de SOLO LECTURA.
// Lee unicamente estructura (nunca registros con datos de personas).
public sealed class LectorA1MotorRelacional : ILectorDeEstructura
{
    private static readonly IReadOnlyList<MetadatoTecnico> EstructuraDeEjemplo = new List<MetadatoTecnico>
    {
        new("clientes", "cedula", "varchar", 20, true),
        new("clientes", "nombre", "varchar", 100, true),
        new("contratos", "vr_can", "decimal", null, true),
        new("contratos", "estado", "varchar", 20, false)
    };

    private readonly EstructurasSimuladasDeFuentes _estructuras;

    public LectorA1MotorRelacional(EstructurasSimuladasDeFuentes estructuras) => _estructuras = estructuras;

    public bool Atiende(TipoDeFuente tipo) => tipo == TipoDeFuente.MotorRelacional;

    public Task<IReadOnlyList<MetadatoTecnico>> LeerEstructura(Fuente fuente, string? credenciales, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credenciales))
            throw new ErrorDeReglaDeNegocio(
                "La fuente no tiene registrada la cuenta de solo lectura necesaria para leer su catalogo.");

        return Task.FromResult(_estructuras.Obtener(fuente.Id) ?? EstructuraDeEjemplo);
    }
}

// SIMULA el adaptador A2 (V4d): el CRM (S2) solo entrega su estructura cuando el PROVEEDOR la envia, y puede tardar dias.
// Mientras el proveedor no entregue, la fuente "no esta disponible" (invariante de la V6.a para S2).
public sealed class LectorA2Crm : ILectorDeEstructura
{
    private readonly EstructurasSimuladasDeFuentes _entregas;

    public LectorA2Crm(EstructurasSimuladasDeFuentes entregas) => _entregas = entregas;

    public bool Atiende(TipoDeFuente tipo) => tipo == TipoDeFuente.SistemaDeProveedor;

    public Task<IReadOnlyList<MetadatoTecnico>> LeerEstructura(Fuente fuente, string? credenciales, CancellationToken ct)
    {
        var entrega = _entregas.Obtener(fuente.Id)
            ?? throw new ErrorDeFuenteNoDisponible(
                "El proveedor del CRM aun no ha entregado la estructura de esta fuente; la entrega depende del proveedor y puede tardar dias.");

        return Task.FromResult(entrega);
    }
}