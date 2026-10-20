namespace FlowTrim.Application.Imports;

/// <summary>Vista previa de una importación CSV (HU-43): nada de esto se ha guardado.</summary>
public sealed record SubscriptionImportPreview(
    IReadOnlyList<ImportRow> Rows,
    ImportSummary Summary,
    IReadOnlyList<string> IgnoredColumns);

/// <summary>Fila del archivo: número de fila de la hoja, estado, valores normalizados (solo si es válida) y errores.</summary>
public sealed record ImportRow(int RowNumber, string Status, ImportRowValues? Values, IReadOnlyList<ImportError> Errors);

/// <summary>Valores ya normalizados, listos para crear la suscripción.</summary>
public sealed record ImportRowValues(
    string Name,
    string? Category,
    decimal Amount,
    string Currency,
    string BillingCycle,
    DateOnly NextBillingDate,
    string Status);

/// <summary>Error de un campo: columna CSV, código estable y mensaje seguro.</summary>
public sealed record ImportError(string Field, string Code, string Message);

public sealed record ImportSummary(int Total, int Valid, int Invalid);

/// <summary>Estados de fila (contrato con HU-44 a HU-46).</summary>
public static class ImportRowStatus
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
}

/// <summary>Códigos estables de error de fila y de archivo.</summary>
public static class ImportErrorCodes
{
    public const string Required = "required";
    public const string TooLong = "too_long";
    public const string InvalidAmount = "invalid_amount";
    public const string AmountNotPositive = "amount_not_positive";
    public const string InvalidCurrency = "invalid_currency";
    public const string InvalidBillingCycle = "invalid_billing_cycle";
    public const string InvalidDate = "invalid_date";
    public const string InvalidStatus = "invalid_status";
    public const string WrongCellCount = "wrong_cell_count";

    public const string FileRequired = "file_required";
    public const string UnsupportedFileType = "unsupported_file_type";
    public const string EmptyFile = "empty_file";
    public const string InvalidEncoding = "invalid_encoding";
    public const string MissingColumns = "missing_columns";
    public const string DuplicateColumns = "duplicate_columns";
    public const string MalformedCsv = "malformed_csv";
    public const string FileTooLarge = "file_too_large";
    public const string TooManyRows = "too_many_rows";
}
