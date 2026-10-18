using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FlowTrim.Api.Observability;

/// <summary>
/// Asigna un identificador de correlación a cada petición, lo devuelve en <c>X-Correlation-ID</c>, lo añade
/// como scope a todos los logs de la petición y registra una línea final con método, ruta, estado y duración.
/// Nunca registra cabeceras, cuerpos ni query strings.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ScopeKey = "CorrelationId";

    private const string HealthRoute = "/api/health";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);

        // TraceIdentifier es lo que usa ProblemDetails para el campo correlationId.
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Scope con plantilla: legible en consola ("CorrelationId:…") y campo propio en JSON.
        using var scope = logger.BeginScope("CorrelationId:{CorrelationId}", correlationId);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            // La plantilla de ruta evita registrar ids reales y nunca incluye la query string.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var level = route == HealthRoute ? LogLevel.Debug : LogLevel.Information;
            var elapsedMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
            logger.Log(level, "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms",
                context.Request.Method, route, context.Response.StatusCode, elapsedMs);
        }
    }

    /// <summary>
    /// Reutiliza el id entrante solo si es seguro (1–64 caracteres de <c>[A-Za-z0-9._-]</c>); si no, genera
    /// uno nuevo. Un valor inseguro nunca se devuelve ni se registra.
    /// </summary>
    public static string ResolveCorrelationId(string? incoming) =>
        incoming is not null && SafeCorrelationId().IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeCorrelationId();
}
