using FlowTrim.Application.Imports;
using Microsoft.AspNetCore.Mvc;

namespace FlowTrim.Api.Imports;

/// <summary>
/// Importación CSV de suscripciones (EP-07). La vista previa (HU-43/HU-44) valida el archivo y marca duplicados sin
/// persistir nada, y nunca registra su contenido: solo los conteos por estado.
/// </summary>
public static class SubscriptionImportEndpoints
{
    // Límite del cuerpo completo (archivo + sobrecarga multipart). El límite del archivo (256 KB) lo aplica el caso de
    // uso con su código propio; esto solo corta subidas abusivas antes de leerlas.
    public const long MaxRequestBytes = 1024 * 1024;

    public static void MapSubscriptionImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/subscription-imports/preview", PreviewAsync)
            .RequireAuthorization()
            // La API se autentica con bearer token y nunca con cookies: no hay CSRF que mitigar con antiforgery.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithName("PreviewSubscriptionImport");
    }

    private static async Task<IResult> PreviewAsync(
        IFormFile? file,
        IPreviewSubscriptionImportHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ProblemResults.WithCode(StatusCodes.Status400BadRequest, ImportErrorCodes.FileRequired,
                "Choose a CSV file to import.", "Send the file in the multipart field 'file'.");
        }

        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(file.FileName, content, cancellationToken);
        if (result.FileError is { } error)
        {
            return ProblemResults.WithCode(
                error.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
                error.Code, "The file cannot be imported.", error.Detail);
        }

        var summary = result.Preview!.Summary;
        loggerFactory.CreateLogger("FlowTrim.Api.Imports").LogInformation(
            "Subscription import preview with {Total} rows ({Valid} valid, {Invalid} invalid, {Duplicate} duplicate)",
            summary.Total, summary.Valid, summary.Invalid, summary.Duplicate);
        return Results.Ok(result.Preview);
    }
}
