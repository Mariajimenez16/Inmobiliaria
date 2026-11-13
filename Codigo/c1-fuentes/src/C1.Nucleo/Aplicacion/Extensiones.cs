using C1.Nucleo.Dominio;
using C1.Nucleo.Puertos.Salida;

namespace C1.Nucleo.Aplicacion;

internal static class RepositoriosExtensiones
{
    public static async Task<Fuente> ObtenerORechazar(this IRepositorioDeFuentes repositorio, FuenteId id, CancellationToken ct) =>
        await repositorio.Obtener(id, ct) ?? throw new ErrorNoEncontrado($"No existe la fuente '{id}'.");
}