namespace FlowTrim.Application.Abstractions;

/// <summary>
/// Comprueba si la base de datos está accesible. La implementación vive en Infrastructure.
/// </summary>
public interface IDatabaseHealthChecker
{
    /// <summary>
    /// Devuelve <c>true</c> si se pudo establecer conexión con la base de datos; de lo contrario, <c>false</c>.
    /// Nunca propaga la excepción: los detalles internos no deben salir de la capa de infraestructura.
    /// </summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
