using System.Text.Json.Serialization;

namespace FlowTrim.Application.Imports;

/// <summary>Vista previa de una importación CSV (HU-43/HU-44): nada de esto se ha guardado.</summary>
public sealed record SubscriptionImportPreview(
    IReadOnlyList<ImportRow> Rows,
    ImportSummary Summary,
    IReadOnlyList<string> IgnoredColumns);

/// <summary>
/// Fila del archivo: número de fila de la hoja, estado, valores normalizados (si no es inválida) y errores. Una fila
/// duplicada (HU-44) indica contra qué coincidió; esos dos campos solo aparecen en filas duplicadas.
/// </summary>
public sealed record ImportRow(
    int RowNumber,
    string Status,
    ImportRowValues? Values,
    IReadOnlyList<ImportError> Errors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DuplicateOf = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DuplicateOfRow = null);

/// <summary>Valores ya normalizados, listos para crear la suscripción. Sin columna <c>kind</c> la fila es "Subscription".</summary>
public sealed record ImportRowValues(
    string Name,
    string? Category,
    decimal Amount,
    string Currency,
    string BillingCycle,
    DateOnly NextBillingDate,
    string Status,
    string Kind = "Subscription",
    string? PaymentUrl = null);

/// <summary>Error de un campo: columna CSV, código estable y mensaje seguro.</summary>
public sealed record ImportError(string Field, string Code, string Message);

/// <summary>Conteos de la vista previa: <c>Valid</c> son las filas que se crearían; siempre suman <c>Total</c>.</summary>
public sealed record ImportSummary(int Total, int Valid, int Invalid, int Duplicate)
{
    public static ImportSummary From(IReadOnlyCollection<ImportRow> rows) => new(
        rows.Count,
        rows.Count(row => row.Status == ImportRowStatus.Valid),
        rows.Count(row => row.Status == ImportRowStatus.Invalid),
        rows.Count(row => row.Status == ImportRowStatus.Duplicate));
}

/// <summary>Estados de fila (contrato con HU-45 y HU-46).</summary>
public static class ImportRowStatus
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Duplicate = "duplicate";
}

/// <summary>Con qué coincidió una fila duplicada (HU-44).</summary>
public static class ImportDuplicateSource
{
    public const string Existing = "existing";
    public const string File = "file";
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
    public const string DateInPast = "date_in_past";
    public const string InvalidStatus = "invalid_status";
    public const string InvalidKind = "invalid_kind";
    public const string InvalidPaymentUrl = "invalid_payment_url";
    public const string WrongCellCount = "wrong_cell_count";

    public const string FileRequired = "file_required";
    public const string UnsupportedFileType = "unsupported_file_type";
    public const string EmptyFile = "empty_file";
    public const string InvalidEncoding = "invalid_encoding";
    public const string MissingColumns = "missing_columns";
    public const string DuplicateColumns = "duplicate_columns";
    public const string MalformedCsv = "malformed_csv";
    public const string UnsupportedDelimiter = "unsupported_delimiter";
    public const string FileTooLarge = "file_too_large";
    public const string TooManyRows = "too_many_rows";
}
