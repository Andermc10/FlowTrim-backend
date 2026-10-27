namespace FlowTrim.Application.Imports;

/// <summary>
/// Lectura del archivo subido, común a la vista previa (HU-43/HU-44) y a la confirmación (HU-45): las reglas de archivo
/// de <see cref="CsvUpload"/> y la validación de filas. Las dos rutas aplican exactamente las mismas reglas.
/// </summary>
public static class SubscriptionImportFile
{
    public const int MaxFileBytes = CsvUpload.MaxFileBytes;
    public const string FileExtension = CsvUpload.FileExtension;

    public static async Task<SubscriptionImportResult> ReadAsync(
        string? fileName, Stream content, DateOnly today, CancellationToken cancellationToken)
    {
        var (text, error) = await CsvUpload.ReadTextAsync(fileName, content, cancellationToken);
        return error is not null ? SubscriptionImportResult.Failure(error) : SubscriptionCsvImport.Parse(text!, today);
    }
}
