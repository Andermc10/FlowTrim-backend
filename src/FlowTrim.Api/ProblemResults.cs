namespace FlowTrim.Api;

/// <summary>
/// ProblemDetails con un <c>code</c> estable que el frontend usa para elegir su mensaje traducido. El
/// <c>correlationId</c> lo añade la personalización global de ProblemDetails.
/// </summary>
public static class ProblemResults
{
    public static IResult WithCode(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
