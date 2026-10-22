using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Health;

namespace FlowTrim.Api.Health;

/// <summary>Disponibilidad de la API y de la base de datos. Pública y sin detalles internos del fallo.</summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", GetHealthAsync).WithName("GetHealth");
    }

    // 200 si todo está bien; 503 si la base de datos no responde.
    private static async Task<IResult> GetHealthAsync(IDatabaseHealthChecker databaseHealthChecker, CancellationToken cancellationToken)
    {
        bool databaseHealthy = await databaseHealthChecker.CanConnectAsync(cancellationToken);
        var status = databaseHealthy ? HealthResponse.Healthy : HealthResponse.Unhealthy;
        var response = new HealthResponse(Status: status, Api: HealthResponse.Healthy, Database: status);

        return databaseHealthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
