using System.Text;

namespace FlowTrim.Application.Imports;

/// <summary>
/// Lectura de un CSV subido, común a las importaciones de suscripciones (EP-07) y de movimientos (EP-16 HU-79):
/// extensión <c>.csv</c>, tamaño máximo, codificación UTF-8 estricta y sin BOM. Ambas aplican exactamente las mismas reglas.
/// </summary>
public static class CsvUpload
{
    public const int MaxFileBytes = 256 * 1024;
    public const string FileExtension = ".csv";

    // UTF-8 estricto: un byte inválido es un error del archivo, no un carácter reemplazado en silencio.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>El texto del archivo, o el error de archivo que impide leerlo.</summary>
    public static async Task<(string? Text, ImportFileError? Error)> ReadTextAsync(
        string? fileName, Stream content, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (null, new ImportFileError(ImportErrorCodes.UnsupportedFileType, "Upload a .csv file."));
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
                return (null, new ImportFileError(
                    ImportErrorCodes.FileTooLarge, $"The file is larger than {MaxFileBytes / 1024} KB.", TooLarge: true));
            }
        }

        if (buffer.Length == 0)
        {
            return (null, new ImportFileError(ImportErrorCodes.EmptyFile, "The file is empty."));
        }

        try
        {
            // El BOM de UTF-8 (lo escribe Excel y nuestro exportador) no forma parte del contenido.
            return (StrictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('﻿'), null);
        }
        catch (DecoderFallbackException)
        {
            return (null, new ImportFileError(ImportErrorCodes.InvalidEncoding, "Save the file as UTF-8 and try again."));
        }
    }
}
