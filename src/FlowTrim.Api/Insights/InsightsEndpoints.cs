using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FlowTrim.Api.Authentication;
using FlowTrim.Application.Insights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FlowTrim.Api.Insights;

/// <summary>
/// Endpoint protegido de preguntas (HU-35): límite por propietario, presupuesto de tiempo y traducción de los fallos
/// del proveedor a 429/502/504 sin detalles internos. Nunca registra la pregunta ni la respuesta.
/// </summary>
public static class InsightsEndpoints
{
    public const string RateLimitPolicy = "insights";

    public const string TimeoutCode = "insights_timeout";
    public const string RateLimitedCode = "insights_rate_limited";
    public const string UnavailableCode = "insights_unavailable";

    private const string TimeoutTitle = "The assistant took too long to answer. Try again.";

    public static IServiceCollection AddInsightsEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InsightsOptions>()
            .Bind(configuration.GetSection(InsightsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<InsightsOptions>, InsightsOptionsValidator>();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(RateLimitPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<InsightsOptions>>().Value.RateLimit;
                // La ruta exige autenticación antes de llegar aquí, así que siempre hay subject.
                var owner = context.User.FindFirstValue(JwtAuthenticationOptions.SubjectClaimType) ?? string.Empty;
                return RateLimitPartition.GetFixedWindowLimiter(owner, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                TimeSpan? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null;
                await WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, RateLimitedCode,
                    "Too many questions. Wait a moment and try again.", retryAfter);
            };
        });

        return services;
    }

    public static void MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/insights/questions", AskAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("AskInsightsQuestion");
    }

    private static async Task<IResult> AskAsync(
        InsightsQuestionRequest request,
        IAnswerQuestionHandler handler,
        IOptions<InsightsOptions> options,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("FlowTrim.Api.Insights");
        var startedAt = Stopwatch.GetTimestamp();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        try
        {
            var result = await handler.HandleAsync(request, budget.Token);
            if (!result.IsSuccess)
            {
                return Results.ValidationProblem(result.Errors);
            }

            // Solo métricas: nunca la pregunta, la respuesta ni los datos de herramientas.
            logger.LogInformation(
                "Insights question {Status} with tools {Tools} in {ModelCalls} model calls ({InputTokens}/{OutputTokens} tokens) in {ElapsedMs:F0} ms",
                result.Value!.Status, string.Join(",", result.Value.ToolsUsed), result.ModelCalls,
                result.InputTokens, result.OutputTokens, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return Results.Ok(result.Value);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Insights question ran out of its {Seconds} s budget", options.Value.RequestTimeoutSeconds);
            return Problem(StatusCodes.Status504GatewayTimeout, TimeoutCode, TimeoutTitle);
        }
        catch (ChatModelException exception)
        {
            var level = exception.Kind is ChatModelErrorKind.Unauthorized or ChatModelErrorKind.InputTooLarge
                ? LogLevel.Error
                : LogLevel.Warning;
            logger.Log(level, "Insights question failed with provider error {Kind}", exception.Kind);

            if (exception is { Kind: ChatModelErrorKind.RateLimited, RetryAfter: { } wait })
            {
                httpContext.Response.Headers.RetryAfter = RetryAfterSeconds(wait);
            }

            return exception.Kind switch
            {
                ChatModelErrorKind.Timeout => Problem(StatusCodes.Status504GatewayTimeout, TimeoutCode, TimeoutTitle),
                ChatModelErrorKind.RateLimited => Problem(StatusCodes.Status429TooManyRequests, RateLimitedCode,
                    "The assistant is busy right now. Wait a moment and try again."),
                _ => Problem(StatusCodes.Status502BadGateway, UnavailableCode,
                    "The assistant is not available right now. Try again later."),
            };
        }
    }

    private static IResult Problem(int status, string code, string title) => ProblemResults.WithCode(status, code, title);

    private static async Task WriteProblemAsync(HttpContext httpContext, int status, string code, string title, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait)
        {
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds(wait);
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    // Retry-After en segundos enteros, redondeando hacia arriba y nunca menos de 1.
    private static string RetryAfterSeconds(TimeSpan wait) =>
        Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
