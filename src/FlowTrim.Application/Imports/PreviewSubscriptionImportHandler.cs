using System.Text;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;

namespace FlowTrim.Application.Imports;

/// <summary>
/// Caso de uso: vista previa de un CSV de suscripciones (HU-43) con duplicados del propietario (HU-44). No persiste nada.
/// </summary>
public interface IPreviewSubscriptionImportHandler
{
    Task<SubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken);
}

public sealed class PreviewSubscriptionImportHandler : IPreviewSubscriptionImportHandler
{
    public const int MaxFileBytes = 256 * 1024;
    public const string FileExtension = ".csv";

    // UTF-8 estricto: un byte inválido es un error del archivo, no un carácter reemplazado en silencio.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public PreviewSubscriptionImportHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(ImportErrorCodes.UnsupportedFileType, "Upload a .csv file.");
        }

        // Se lee como mucho un byte más del límite: basta para saber que lo excede sin cargar el resto.
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes)
            {
                return SubscriptionImportResult.Failure(new ImportFileError(
                    ImportErrorCodes.FileTooLarge, $"The file is larger than {MaxFileBytes / 1024} KB.", TooLarge: true));
            }
        }

        if (buffer.Length == 0)
        {
            return Fail(ImportErrorCodes.EmptyFile, "The file is empty.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (DecoderFallbackException)
        {
            return Fail(ImportErrorCodes.InvalidEncoding, "Save the file as UTF-8 and try again.");
        }

        // El BOM de UTF-8 (lo escribe Excel y nuestro exportador) no forma parte del contenido.
        var result = SubscriptionCsvImport.Parse(text.TrimStart('﻿'));
        if (result.Preview is not { } preview || preview.Summary.Valid == 0)
        {
            return result;
        }

        // Una sola lectura del propietario; la comparación se hace en memoria (como mucho 500 filas).
        var existing = await _repository.ListAllAsync(_currentUser.GetOwnerId(), cancellationToken);
        return SubscriptionImportResult.Success(SubscriptionImportDuplicates.Classify(preview, existing));
    }

    private static SubscriptionImportResult Fail(string code, string detail) =>
        SubscriptionImportResult.Failure(new ImportFileError(code, detail));
}
