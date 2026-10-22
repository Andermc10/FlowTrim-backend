using FlowTrim.Application.Common;

namespace FlowTrim.Api;

/// <summary>
/// Respuestas de error comunes. ProblemDetails con un <c>code</c> estable que el frontend usa para elegir su mensaje
/// traducido; el <c>correlationId</c> lo añade la personalización global de ProblemDetails.
/// </summary>
public static class ProblemResults
{
    public static IResult WithCode(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>404 sin detalles: un recurso de otro propietario responde igual que uno inexistente.</summary>
    public static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound);

    /// <summary>Traduce el resultado de un caso de uso: 404, 400 con errores por campo o la respuesta de éxito.</summary>
    public static IResult ToHttpResult<T>(this HandlerResult<T> result, Func<T, IResult> onSuccess)
        where T : class => result.Kind switch
        {
            HandlerResultKind.NotFound => NotFound(),
            HandlerResultKind.Invalid => Results.ValidationProblem(result.Errors),
            _ => onSuccess(result.Value!),
        };
}
