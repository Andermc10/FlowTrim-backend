using System.Globalization;
using System.Text.RegularExpressions;
using FlowTrim.Application.Reports;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Imports;

/// <summary>Error que invalida el archivo completo (no una fila). <c>TooLarge</c> se responde como 413.</summary>
public sealed record ImportFileError(string Code, string Detail, bool TooLarge = false);

/// <summary>Resultado de leer y validar un archivo: la vista previa o un error de archivo.</summary>
public sealed class SubscriptionImportResult
{
    public SubscriptionImportPreview? Preview { get; }
    public ImportFileError? FileError { get; }

    private SubscriptionImportResult(SubscriptionImportPreview? preview, ImportFileError? fileError)
    {
        Preview = preview;
        FileError = fileError;
    }

    public static SubscriptionImportResult Success(SubscriptionImportPreview preview) => new(preview, null);

    public static SubscriptionImportResult Failure(ImportFileError error) => new(null, error);
}

/// <summary>
/// Lee y valida el CSV de suscripciones (HU-43, decisiones D1–D3 de EP-07) sin persistir nada. Las reglas de campo son
/// las del dominio (<see cref="Subscription"/>); aquí solo se añaden las reglas de formato del archivo.
/// </summary>
public static partial class SubscriptionCsvImport
{
    public const int MaxRows = 500;

    public static readonly IReadOnlyList<string> RequiredColumns =
    [
        SubscriptionCsvFormat.Name, SubscriptionCsvFormat.Amount, SubscriptionCsvFormat.Currency,
        SubscriptionCsvFormat.BillingCycle, SubscriptionCsvFormat.NextBillingDate,
    ];

    private static readonly IReadOnlyList<string> OptionalColumns = [SubscriptionCsvFormat.Category, SubscriptionCsvFormat.Status];

    // Punto decimal, como mucho 2 decimales, sin separador de miles ni símbolo de moneda.
    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex AmountPattern();

    [GeneratedRegex("^[A-Za-z]+$")]
    private static partial Regex LettersOnly();

    public static SubscriptionImportResult Parse(string text)
    {
        IReadOnlyList<CsvRecord> records;
        try
        {
            records = CsvReader.Read(text);
        }
        catch (CsvFormatException exception)
        {
            return Fail(ImportErrorCodes.MalformedCsv, exception.Message);
        }

        var content = records.Where(record => !record.IsBlank).ToList();
        if (content.Count == 0)
        {
            return Fail(ImportErrorCodes.EmptyFile, "The file has no header row.");
        }

        var header = content[0].Cells.Select(name => name.Trim().ToLowerInvariant()).ToList();
        var repeated = header.Where(name => name.Length > 0).GroupBy(name => name).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToList();
        if (repeated.Count > 0)
        {
            return Fail(ImportErrorCodes.DuplicateColumns, $"Columns appear more than once: {string.Join(", ", repeated)}.");
        }

        var missing = RequiredColumns.Where(column => !header.Contains(column)).ToList();
        if (missing.Count > 0)
        {
            return Fail(ImportErrorCodes.MissingColumns, $"Missing required columns: {string.Join(", ", missing)}.");
        }

        var dataRows = content.Skip(1).ToList();
        if (dataRows.Count > MaxRows)
        {
            return SubscriptionImportResult.Failure(new ImportFileError(
                ImportErrorCodes.TooManyRows, $"The file has more than {MaxRows} rows.", TooLarge: true));
        }

        // Índice de cada columna conocida, calculado una vez por archivo.
        var columns = header.Select((name, index) => (name, index)).Where(column => column.name.Length > 0)
            .ToDictionary(column => column.name, column => column.index);
        var rows = dataRows.Select(record => ValidateRow(record, header.Count, columns)).ToList();
        var valid = rows.Count(row => row.Status == ImportRowStatus.Valid);
        var ignored = header.Where(name => name.Length > 0 && !RequiredColumns.Contains(name) && !OptionalColumns.Contains(name)).ToList();
        return SubscriptionImportResult.Success(new SubscriptionImportPreview(
            rows, new ImportSummary(rows.Count, valid, rows.Count - valid), ignored));
    }

    private static ImportRow ValidateRow(CsvRecord record, int columnCount, IReadOnlyDictionary<string, int> columns)
    {
        if (record.Cells.Count != columnCount)
        {
            return Invalid(record, [new ImportError("row", ImportErrorCodes.WrongCellCount,
                $"The row has {record.Cells.Count} cells but the header has {columnCount}.")]);
        }

        string Cell(string column) => columns.TryGetValue(column, out var index) ? Clean(record.Cells[index]) : string.Empty;

        var errors = new List<ImportError>();
        var name = Text(Cell(SubscriptionCsvFormat.Name), SubscriptionCsvFormat.Name, Subscription.NameMaxLength, required: true, errors);
        var category = Text(Cell(SubscriptionCsvFormat.Category), SubscriptionCsvFormat.Category, Subscription.CategoryMaxLength, required: false, errors);
        var amount = Amount(Cell(SubscriptionCsvFormat.Amount), errors);
        var currency = Currency(Cell(SubscriptionCsvFormat.Currency), errors);
        var cycle = Enumeration<BillingCycle>(Cell(SubscriptionCsvFormat.BillingCycle), SubscriptionCsvFormat.BillingCycle,
            ImportErrorCodes.InvalidBillingCycle, "Use Monthly or Yearly.", required: true, errors);
        var date = Date(Cell(SubscriptionCsvFormat.NextBillingDate), errors);
        var status = Enumeration<SubscriptionStatus>(Cell(SubscriptionCsvFormat.Status), SubscriptionCsvFormat.Status,
            ImportErrorCodes.InvalidStatus, "Use Active or Inactive.", required: false, errors) ?? SubscriptionStatus.Active;

        if (errors.Count > 0)
        {
            return Invalid(record, errors);
        }

        var values = new ImportRowValues(name!, category, amount!.Value, currency!, cycle!.Value.ToString(), date!.Value, status.ToString());
        return new ImportRow(record.RowNumber, ImportRowStatus.Valid, values, []);
    }

    // Quita el apóstrofo que el exportador antepone a una celda con prefijo de fórmula y recorta espacios.
    private static string Clean(string cell)
    {
        var unguarded = cell.Length > 1 && cell[0] == SubscriptionCsvFormat.FormulaGuard
            && SubscriptionCsvFormat.FormulaPrefixes.Contains(cell[1])
                ? cell[1..]
                : cell;
        return unguarded.Trim();
    }

    private static string? Text(string value, string field, int maxLength, bool required, List<ImportError> errors)
    {
        if (value.Length == 0)
        {
            if (required)
            {
                errors.Add(new ImportError(field, ImportErrorCodes.Required, $"The {field} is required."));
            }

            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add(new ImportError(field, ImportErrorCodes.TooLong, $"Use at most {maxLength} characters."));
        }

        return value;
    }

    private static decimal? Amount(string value, List<ImportError> errors)
    {
        if (value.Length == 0)
        {
            errors.Add(new ImportError(SubscriptionCsvFormat.Amount, ImportErrorCodes.Required, "The amount is required."));
            return null;
        }

        if (!AmountPattern().IsMatch(value))
        {
            errors.Add(new ImportError(SubscriptionCsvFormat.Amount, ImportErrorCodes.InvalidAmount,
                "Use digits with a dot for decimals (at most 2) and no thousands separators."));
            return null;
        }

        var amount = decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (amount <= 0m)
        {
            errors.Add(new ImportError(SubscriptionCsvFormat.Amount, ImportErrorCodes.AmountNotPositive, "The amount must be greater than zero."));
            return null;
        }

        return amount;
    }

    private static string? Currency(string value, List<ImportError> errors)
    {
        var currency = value.ToUpperInvariant();
        if (Subscription.IsValidCurrencyFormat(currency))
        {
            return currency;
        }

        errors.Add(value.Length == 0
            ? new ImportError(SubscriptionCsvFormat.Currency, ImportErrorCodes.Required, "The currency is required.")
            : new ImportError(SubscriptionCsvFormat.Currency, ImportErrorCodes.InvalidCurrency, "Use a 3-letter ISO 4217 code, for example COP."));
        return null;
    }

    // Solo nombres en letras (Enum.TryParse también aceptaría "1"), sin distinguir mayúsculas.
    private static TEnum? Enumeration<TEnum>(string value, string field, string code, string hint, bool required, List<ImportError> errors)
        where TEnum : struct, Enum
    {
        if (value.Length == 0)
        {
            if (required)
            {
                errors.Add(new ImportError(field, ImportErrorCodes.Required, $"The {field} is required. {hint}"));
            }

            return null;
        }

        if (LettersOnly().IsMatch(value) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ImportError(field, code, hint));
        return null;
    }

    private static DateOnly? Date(string value, List<ImportError> errors)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(value.Length == 0
            ? new ImportError(SubscriptionCsvFormat.NextBillingDate, ImportErrorCodes.Required, "The next billing date is required.")
            : new ImportError(SubscriptionCsvFormat.NextBillingDate, ImportErrorCodes.InvalidDate, "Use a real date in yyyy-MM-dd format."));
        return null;
    }

    private static ImportRow Invalid(CsvRecord record, IReadOnlyList<ImportError> errors) =>
        new(record.RowNumber, ImportRowStatus.Invalid, null, errors);

    private static SubscriptionImportResult Fail(string code, string detail) =>
        SubscriptionImportResult.Failure(new ImportFileError(code, detail));
}
