using FlowTrim.Application.Imports;
using Microsoft.AspNetCore.Mvc;

namespace FlowTrim.Api.Imports;

/// <summary>
/// Importación CSV de suscripciones (EP-07). La vista previa (HU-43/HU-44) valida el archivo y marca duplicados sin
/// persistir nada; la confirmación (HU-45) recibe el mismo archivo y crea las filas válidas en una transacción. Nunca
/// se registra el contenido del archivo: solo los conteos por estado.
/// </summary>
public static class SubscriptionImportEndpoints
{
    // Límite del cuerpo completo (archivo + sobrecarga multipart). El límite del archivo (256 KB) lo aplica el caso de
    // uso con su código propio; esto solo corta subidas abusivas antes de leerlas.
    public const long MaxRequestBytes = 1024 * 1024;

    private const string LoggerName = "FlowTrim.Api.Imports";

    public static void MapSubscriptionImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/subscription-imports")
            .RequireAuthorization()
            // La API se autentica con bearer token y nunca con cookies: no hay CSRF que mitigar con antiforgery.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));

        group.MapPost("/preview", PreviewAsync).WithName("PreviewSubscriptionImport");
        group.MapPost("/confirm", ConfirmAsync).WithName("ConfirmSubscriptionImport");
    }

    private static async Task<IResult> PreviewAsync(
        IFormFile? file,
        IPreviewSubscriptionImportHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileRequired();
        }

        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(file.FileName, content, cancellationToken);
        if (result.FileError is { } error)
        {
            return FileProblem(error);
        }

        var summary = result.Preview!.Summary;
        loggerFactory.CreateLogger(LoggerName).LogInformation(
            "Subscription import preview with {Total} rows ({Valid} valid, {Invalid} invalid, {Duplicate} duplicate)",
            summary.Total, summary.Valid, summary.Invalid, summary.Duplicate);
        return Results.Ok(result.Preview);
    }

    private static async Task<IResult> ConfirmAsync(
        IFormFile? file,
        IConfirmSubscriptionImportHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileRequired();
        }

        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(file.FileName, content, cancellationToken);
        if (result.FileError is { } error)
        {
            return FileProblem(error);
        }

        var confirmation = result.Confirmation!;
        loggerFactory.CreateLogger(LoggerName).LogInformation(
            "Subscription import confirmed with {Total} rows ({Created} created, {Invalid} invalid, {Duplicate} duplicate)",
            confirmation.Total, confirmation.Created, confirmation.Invalid, confirmation.Duplicate);
        return Results.Ok(confirmation);
    }

    private static IResult FileRequired() =>
        ProblemResults.WithCode(StatusCodes.Status400BadRequest, ImportErrorCodes.FileRequired,
            "Choose a CSV file to import.", "Send the file in the multipart field 'file'.");

    private static IResult FileProblem(ImportFileError error) =>
        ProblemResults.WithCode(
            error.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
            error.Code, "The file cannot be imported.", error.Detail);
}
