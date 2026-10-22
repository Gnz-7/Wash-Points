using WashPoints.Domain.Geo;

namespace WashPoints.Infrastructure.Geo;

/// <summary>
/// Implementación por defecto mientras no se elija un proveedor de mapas
/// (ARCHITECTURE §7.4). Falla de forma explícita y nunca devuelve un resultado
/// vacío: un vacío significaría "no hay lavaderos en la zona" y confundiría al
/// cliente (US-C07 CA-7.4).
/// </summary>
public sealed class GeocodificadorStub : IGeocodificador
{
    public Task<Coordenadas?> GeocodificarAsync(string direccion, CancellationToken ct = default) =>
        Task.FromException<Coordenadas?>(new ProveedorMapasNoConfiguradoException(
            "La búsqueda por dirección no está disponible: el proveedor de mapas no está configurado."));
}
