using FlowTrim.Api.Accounts;
using FlowTrim.Api.Authentication;
using FlowTrim.Api.Budgets;
using FlowTrim.Api.Calendar;
using FlowTrim.Api.Categories;
using FlowTrim.Api.Dashboard;
using FlowTrim.Api.Cors;
using FlowTrim.Api.Health;
using FlowTrim.Api.Imports;
using FlowTrim.Api.Insights;
using FlowTrim.Api.Observability;
using FlowTrim.Api.Spending;
using FlowTrim.Api.Subscriptions;
using FlowTrim.Api.Transactions;
using FlowTrim.Application;
using FlowTrim.Application.Abstractions;
using FlowTrim.Infrastructure;
using FlowTrim.Infrastructure.Insights;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Logs: fuera de desarrollo, una línea JSON por evento (UTC, con scopes como CorrelationId) para poder
// filtrarlos por campo; en desarrollo, consola legible. Sin librerías externas.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

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

// Proveedor de IA (HU-33): simulado por defecto; el adaptador real exige BaseUrl https, modelo y clave al arrancar.
builder.Services.AddOptions<ChatModelOptions>()
    .Bind(builder.Configuration.GetSection(ChatModelOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ChatModelOptions>, ChatModelOptionsValidator>();
builder.Services.AddChatModel();
builder.Services.AddApplication();

// Preguntas de Insights (HU-35): límites validados al arrancar y rate limit por propietario.
builder.Services.AddInsightsEndpoints(builder.Configuration);

// Manejo centralizado de errores: ProblemDetails consistente + handler global.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        // Un único identificador visible para el cliente: el mismo de la cabecera X-Correlation-ID.
        context.ProblemDetails.Extensions.Remove("traceId");
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    });
builder.Services.AddExceptionHandler<FlowTrim.Api.GlobalExceptionHandler>();

// Peticiones ilegibles (JSON roto, tipos incorrectos): por defecto las minimal APIs lanzan la excepción solo en
// desarrollo y fuera responden 400 sin cuerpo. Se lanza siempre para que el manejador global responda igual en
// todos los entornos: 400 ProblemDetails con correlationId (backend#74).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

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
                .WithHeaders("Accept", "Content-Type", "Authorization")
                // El navegador solo deja leer cabeceras expuestas: Angular muestra la referencia del error, la espera
                // de Insights y el nombre del CSV exportado (sin Content-Disposition tendría que inventarlo).
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Retry-After", "Content-Disposition")));

var app = builder.Build();

// La correlación va primero: así el scope y la cabecera también cubren los errores 500.
app.UseMiddleware<CorrelationIdMiddleware>();

// El manejador de excepciones va justo después para capturar cualquier fallo del pipeline.
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
// Después de la autorización: el límite por propietario necesita el usuario autenticado.
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapSubscriptionEndpoints();
app.MapSpendingEndpoints();
app.MapInsightsEndpoints();
app.MapSubscriptionImportEndpoints();
app.MapAccountEndpoints();
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapDashboardEndpoints();
app.MapCalendarEndpoints();
app.MapBudgetEndpoints();

app.Run();

// Expone la clase Program a WebApplicationFactory en las pruebas de integración HTTP.
public partial class Program;
