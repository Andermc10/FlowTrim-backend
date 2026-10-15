using FlowTrim.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlowTrim.Api;

/// <summary>
/// Manejador global de excepciones. Traduce las violaciones de invariantes de dominio a un 400
/// con el formato de validación por campo, y cualquier otra excepción a un 500 genérico (registrado
/// en el servidor, sin stack trace ni secretos para el cliente).
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DomainValidationException domainValidation)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            var validationProblem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            };
            validationProblem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                [domainValidation.PropertyName] = [domainValidation.Message]
            };

            return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = validationProblem
            });
        }

        // No se expone el detalle: se registra en el servidor y se devuelve un 500 genérico.
        _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred."
            }
        });
    }
}
