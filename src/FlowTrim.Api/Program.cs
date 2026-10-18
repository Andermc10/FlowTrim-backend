using FlowTrim.Api.Authentication;
using FlowTrim.Api.Cors;
using FlowTrim.Application;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Health;
using FlowTrim.Application.Simulations;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Summaries;
using FlowTrim.Application.Usage;
using FlowTrim.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Documentación OpenAPI (solo se expone en desarrollo más abajo).
builder.Services.AddOpenApi();

// Authority y Audience se validan al arrancar (no al construir el host), para que herramientas como
// `dotnet ef` sigan funcionando sin configuración de identidad.
builder.Services.AddOptions<JwtAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(JwtAuthenticationOptions.SectionName))
    .Validate(
        options => options.IsConfigured(),
        "Authentication:Authority and Authentication:Audience must be configured (User Secrets or environment variables).")
    .ValidateOnStart();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// JwtBearer valida firma (JWKS del proveedor), issuer, audience y expiración; nunca se valida a mano.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtAuthenticationOptions>>((bearer, authentication) =>
    {
        bearer.Authority = authentication.Value.Authority;
        bearer.Audience = authentication.Value.Audience;
        bearer.RequireHttpsMetadata = authentication.Value.RequireHttpsMetadata;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = JwtAuthenticationOptions.SubjectClaimType,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

// Toda ruta con RequireAuthorization exige usuario autenticado con un subject estable.
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(JwtAuthenticationOptions.SubjectClaimType)
        .Build());

// La cadena de conexión nunca se versiona: viene de User Secrets (dev) o variables de entorno (deploy).
var connectionString = builder.Configuration.GetConnectionString("FlowTrimDb")
    ?? throw new InvalidOperationException(
        "Connection string 'FlowTrimDb' is not configured. Set it via User Secrets or the ConnectionStrings__FlowTrimDb environment variable.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();

// Manejo centralizado de errores: ProblemDetails consistente + handler global.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FlowTrim.Api.GlobalExceptionHandler>();

// CORS: solo los orígenes configurados, validados al arrancar. Sin valor por defecto en el código:
// desarrollo lo define en appsettings.Development.json y producción por variables de entorno.
builder.Services.AddOptions<AllowedOriginsOptions>()
    .Bind(builder.Configuration.GetSection(AllowedOriginsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AllowedOriginsOptions>, AllowedOriginsValidator>();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<AllowedOriginsOptions>>((cors, allowed) =>
        cors.AddPolicy(AllowedOriginsOptions.PolicyName, policy =>
            policy.WithOrigins(allowed.Value.AllowedOrigins)
                .WithMethods("GET", "POST", "PUT", "DELETE")
                // El token viaja en la cabecera Authorization; no se permiten cookies (sin AllowCredentials).
                .WithHeaders("Accept", "Content-Type", "Authorization")));

var app = builder.Build();

// El manejador de excepciones va primero para capturar cualquier fallo del pipeline.
app.UseExceptionHandler();

// Configuración del pipeline HTTP.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// En desarrollo trabajamos sobre HTTP para que el preflight CORS no se redirija entre esquemas.
// Fuera de desarrollo: HSTS indica al navegador que use siempre HTTPS con este host.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(AllowedOriginsOptions.PolicyName);
app.UseAuthentication();
app.UseAuthorization();

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
.RequireAuthorization()
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
.RequireAuthorization()
.WithName("ListSubscriptions");

// Próximos cobros (activas) ordenados por fecha, con cuenta regresiva y ventana de alerta.
app.MapGet("/api/subscriptions/upcoming", async (
    IGetUpcomingChargesHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(cancellationToken);
    return Results.Ok(result);
})
.RequireAuthorization()
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
.RequireAuthorization()
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
.RequireAuthorization()
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
.RequireAuthorization()
.WithName("DeleteSubscription");

// Resumen financiero por moneda (equivalente mensual y proyección anual de las suscripciones activas).
app.MapGet("/api/summary", async (
    IGetSummaryHandler handler,
    CancellationToken cancellationToken) =>
{
    var summary = await handler.HandleAsync(cancellationToken);
    return Results.Ok(summary);
})
.RequireAuthorization()
.WithName("GetSummary");

// Registrar un uso declarado para una suscripción. 201 / 400 / 404 (si la suscripción no existe).
app.MapPost("/api/subscriptions/{id:guid}/usage-records", async (
    Guid id,
    RegisterUsageRequest request,
    IRegisterUsageHandler handler,
    CancellationToken cancellationToken) =>
{
    RegisterUsageResult result = await handler.HandleAsync(id, request, cancellationToken);

    return result.Kind switch
    {
        RegisterUsageResult.ResultKind.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        RegisterUsageResult.ResultKind.Invalid => Results.ValidationProblem(result.Errors),
        _ => Results.Created($"/api/subscriptions/{id}/usage-records/{result.Value!.Id}", result.Value),
    };
})
.RequireAuthorization()
.WithName("RegisterUsage");

// Listar los usos de una suscripción, filtrables por periodo. 200 / 404.
app.MapGet("/api/subscriptions/{id:guid}/usage-records", async (
    Guid id,
    IListUsageHandler handler,
    CancellationToken cancellationToken,
    DateOnly? from,
    DateOnly? to) =>
{
    var records = await handler.HandleAsync(id, from, to, cancellationToken);
    return records is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(records);
})
.RequireAuthorization()
.WithName("ListUsage");

// Calcula el costo declarado por uso. 200 con resultado o 404 si la suscripción no existe.
app.MapGet("/api/subscriptions/{id:guid}/cost-per-use", async (
    Guid id,
    IGetCostPerUseHandler handler,
    CancellationToken cancellationToken,
    DateOnly? from,
    DateOnly? to) =>
{
    var result = await handler.HandleAsync(id, from, to, cancellationToken);
    return result is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(result);
})
.RequireAuthorization()
.WithName("GetCostPerUse");

// Simulación de cancelación y ahorro potencial. No persiste nada. 200 con el resultado, o 400 si los horizontes son inválidos.
app.MapPost("/api/simulations", async (
    SimulationRequest request,
    ISimulateHandler handler,
    CancellationToken cancellationToken) =>
{
    SimulationResult result = await handler.HandleAsync(request, cancellationToken);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.ValidationProblem(result.Errors);
})
.RequireAuthorization()
.WithName("Simulate");

app.Run();

// Expone la clase Program a WebApplicationFactory en las pruebas de integración HTTP.
public partial class Program;
