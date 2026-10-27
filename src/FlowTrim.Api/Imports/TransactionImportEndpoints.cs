using FlowTrim.Application.Imports;
using FlowTrim.Application.TransactionImports;
using Microsoft.AspNetCore.Mvc;

namespace FlowTrim.Api.Imports;

/// <summary>
/// Importación del CSV de un banco a una cuenta (EP-16 HU-79). Multipart con <c>file</c>, <c>accountId</c>,
/// <c>expenseCategoryId</c> e <c>incomeCategoryId</c>. La vista previa no persiste; la confirmación reenvía lo mismo y crea
/// las filas válidas en una transacción. Nunca se registra el contenido del archivo: solo conteos.
/// </summary>
public static class TransactionImportEndpoints
{
    private const string LoggerName = "FlowTrim.Api.Imports";

    public static void MapTransactionImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transaction-imports")
            .RequireAuthorization()
            // La API se autentica con bearer token y nunca con cookies: no hay CSRF que mitigar con antiforgery.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(SubscriptionImportEndpoints.MaxRequestBytes));

        group.MapPost("/preview", PreviewAsync).WithName("PreviewTransactionImport");
        group.MapPost("/confirm", ConfirmAsync).WithName("ConfirmTransactionImport");
    }

    private static async Task<IResult> PreviewAsync(
        IFormFile? file,
        [FromForm] Guid? accountId,
        [FromForm] Guid? expenseCategoryId,
        [FromForm] Guid? incomeCategoryId,
        IPreviewTransactionImportHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileRequired();
        }

        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(
            file.FileName, content, new TransactionImportChoices(accountId, expenseCategoryId, incomeCategoryId), cancellationToken);
        if (Failure(result) is { } failure)
        {
            return failure;
        }

        var summary = result.Preview!.Summary;
        loggerFactory.CreateLogger(LoggerName).LogInformation(
            "Transaction import preview with {Total} rows ({Valid} valid, {Invalid} invalid, {Duplicate} duplicate)",
            summary.Total, summary.Valid, summary.Invalid, summary.Duplicate);
        return Results.Ok(result.Preview);
    }

    private static async Task<IResult> ConfirmAsync(
        IFormFile? file,
        [FromForm] Guid? accountId,
        [FromForm] Guid? expenseCategoryId,
        [FromForm] Guid? incomeCategoryId,
        IConfirmTransactionImportHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileRequired();
        }

        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(
            file.FileName, content, new TransactionImportChoices(accountId, expenseCategoryId, incomeCategoryId), cancellationToken);
        if (Failure(result) is { } failure)
        {
            return failure;
        }

        var confirmation = result.Confirmation!;
        loggerFactory.CreateLogger(LoggerName).LogInformation(
            "Transaction import confirmed with {Total} rows ({Created} created, {Invalid} invalid, {Duplicate} duplicate)",
            confirmation.Total, confirmation.Created, confirmation.Invalid, confirmation.Duplicate);
        return Results.Ok(confirmation);
    }

    // Elecciones inválidas → 400 por campo; errores de archivo → 400/413 con código, igual que EP-07.
    private static IResult? Failure(TransactionImportResult result) =>
        result.FieldErrors is { } errors
            ? Results.ValidationProblem(errors)
            : result.FileError is { } error
                ? ProblemResults.WithCode(
                    error.TooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
                    error.Code, "The file cannot be imported.", error.Detail)
                : null;

    private static IResult FileRequired() =>
        ProblemResults.WithCode(StatusCodes.Status400BadRequest, ImportErrorCodes.FileRequired,
            "Choose a CSV file to import.", "Send the file in the multipart field 'file'.");
}
