using FlowTrim.Application;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Health;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Summaries;
using FlowTrim.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Documentación OpenAPI (solo se expone en desarrollo más abajo).
builder.Services.AddOpenApi();

// La cadena de conexión nunca se versiona: viene de User Secrets (dev) o variables de entorno (deploy).
var connectionString = builder.Configuration.GetConnectionString("FlowTrimDb")
    ?? throw new InvalidOperationException(
        "Connection string 'FlowTrimDb' is not configured. Set it via User Secrets or the ConnectionStrings__FlowTrimDb environment variable.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();

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

// Crea una suscripción. Devuelve 201 con el recurso creado, o 400 con errores por campo.
app.MapPost("/api/subscriptions", async (
    CreateSubscriptionRequest request,
    ICreateSubscriptionHandler handler,
    CancellationToken cancellationToken) =>
{
    CreateSubscriptionResult result = await handler.HandleAsync(request, cancellationToken);

    return result.IsSuccess
        ? Results.Created($"/api/subscriptions/{result.Value!.Id}", result.Value)
        : Results.ValidationProblem(result.Errors);
})
.WithName("CreateSubscription");

// Lista paginada de suscripciones (orden: próxima fecha de cobro, luego nombre).
app.MapGet("/api/subscriptions", async (
    IListSubscriptionsHandler handler,
    CancellationToken cancellationToken,
    int? page,
    int? pageSize) =>
{
    var result = await handler.HandleAsync(page ?? 1, pageSize ?? ListSubscriptionsHandler.DefaultPageSize, cancellationToken);
    return Results.Ok(result);
})
.WithName("ListSubscriptions");

// Próximos cobros (activas) ordenados por fecha, con cuenta regresiva y ventana de alerta.
app.MapGet("/api/subscriptions/upcoming", async (
    IGetUpcomingChargesHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(cancellationToken);
    return Results.Ok(result);
})
.WithName("GetUpcomingCharges");

// Detalle de una suscripción.
app.MapGet("/api/subscriptions/{id:guid}", async (
    Guid id,
    IGetSubscriptionHandler handler,
    CancellationToken cancellationToken) =>
{
    var subscription = await handler.HandleAsync(id, cancellationToken);
    return subscription is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(subscription);
})
.WithName("GetSubscription");

// Actualización completa de una suscripción.
app.MapPut("/api/subscriptions/{id:guid}", async (
    Guid id,
    UpdateSubscriptionRequest request,
    IUpdateSubscriptionHandler handler,
    CancellationToken cancellationToken) =>
{
    UpdateSubscriptionResult result = await handler.HandleAsync(id, request, cancellationToken);

    return result.Kind switch
    {
        UpdateSubscriptionResult.ResultKind.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        UpdateSubscriptionResult.ResultKind.Invalid => Results.ValidationProblem(result.Errors),
        _ => Results.Ok(result.Value),
    };
})
.WithName("UpdateSubscription");

// Eliminación de una suscripción.
app.MapDelete("/api/subscriptions/{id:guid}", async (
    Guid id,
    IDeleteSubscriptionHandler handler,
    CancellationToken cancellationToken) =>
{
    bool deleted = await handler.HandleAsync(id, cancellationToken);
    return deleted
        ? Results.NoContent()
        : Results.Problem(statusCode: StatusCodes.Status404NotFound);
})
.WithName("DeleteSubscription");

// Resumen financiero por moneda (equivalente mensual y proyección anual de las suscripciones activas).
app.MapGet("/api/summary", async (
    IGetSummaryHandler handler,
    CancellationToken cancellationToken) =>
{
    var summary = await handler.HandleAsync(cancellationToken);
    return Results.Ok(summary);
})
.WithName("GetSummary");

app.Run();
