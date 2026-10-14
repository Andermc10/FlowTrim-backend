using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Health;
using FlowTrim.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Documentación OpenAPI (solo se expone en desarrollo más abajo).
builder.Services.AddOpenApi();

// La cadena de conexión nunca se versiona: viene de User Secrets (dev) o variables de entorno (deploy).
var connectionString = builder.Configuration.GetConnectionString("FlowTrimDb")
    ?? throw new InvalidOperationException(
        "Connection string 'FlowTrimDb' is not configured. Set it via User Secrets or the ConnectionStrings__FlowTrimDb environment variable.");

builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();

// Configuración del pipeline HTTP.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Comprueba disponibilidad de API y base de datos. Devuelve 200 si todo está bien; 503 si la BD no responde.
// Nunca expone detalles internos del fallo.
app.MapGet("/api/health", async (IDatabaseHealthChecker databaseHealthChecker, CancellationToken cancellationToken) =>
{
    bool databaseHealthy = await databaseHealthChecker.CanConnectAsync(cancellationToken);

    var response = new HealthResponse(
        Status: databaseHealthy ? HealthResponse.Healthy : HealthResponse.Unhealthy,
        Api: HealthResponse.Healthy,
        Database: databaseHealthy ? HealthResponse.Healthy : HealthResponse.Unhealthy);

    return databaseHealthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("GetHealth");

app.Run();
