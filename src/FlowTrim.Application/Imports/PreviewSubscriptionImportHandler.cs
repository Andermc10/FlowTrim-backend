using System.Text;

namespace FlowTrim.Application.Imports;

/// <summary>Caso de uso: vista previa de un CSV de suscripciones (HU-43). No persiste nada.</summary>
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
        return SubscriptionCsvImport.Parse(text.TrimStart('﻿'));
    }

    private static SubscriptionImportResult Fail(string code, string detail) =>
        SubscriptionImportResult.Failure(new ImportFileError(code, detail));
}
