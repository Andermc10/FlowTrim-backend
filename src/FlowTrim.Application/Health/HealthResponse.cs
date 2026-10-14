namespace FlowTrim.Application.Health;

/// <summary>
/// Respuesta pública del endpoint de health. Solo expone estados agregados, nunca detalles internos.
/// </summary>
/// <param name="Status">Estado global: "Healthy" o "Unhealthy".</param>
/// <param name="Api">Estado de la API (siempre "Healthy" si la petición llegó hasta aquí).</param>
/// <param name="Database">Estado de la base de datos: "Healthy" o "Unhealthy".</param>
public sealed record HealthResponse(string Status, string Api, string Database)
{
    // Constantes de estado para evitar literales mágicos repartidos por el código.
    public const string Healthy = "Healthy";
    public const string Unhealthy = "Unhealthy";
}
