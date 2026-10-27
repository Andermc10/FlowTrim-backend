using System.Globalization;
using System.Text.RegularExpressions;
using FlowTrim.Application.Imports;
using FlowTrim.Application.Reports;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.TransactionImports;

/// <summary>
/// Lee y valida el CSV de un banco (EP-16 HU-79, D3/D4/D7) sin persistir nada. Importe con signo: negativo es gasto y
/// positivo ingreso. La columna <c>category</c> usa una categoría activa de esa clase si existe; si no, la de reserva.
/// Los duplicados se marcan aparte (<see cref="BankCsvDuplicates"/>).
/// </summary>
public static partial class BankCsvImport
{
    // Signo opcional, solo dígitos y como mucho 2 decimales con punto; sin separador de miles ni símbolo.
    [GeneratedRegex(@"^[+-]?\d+(\.\d{1,2})?$")]
    private static partial Regex AmountPattern();

    // En archivos con ";" (Excel en español) la coma también es decimal.
    [GeneratedRegex(@"^[+-]?\d+([.,]\d{1,2})?$")]
    private static partial Regex SpreadsheetAmountPattern();

    public static (TransactionImportPreview? Preview, ImportFileError? Error) Parse(
        string text, DateOnly today, TransactionImportCategories categories)
    {
        var (dialect, content) = CsvDialect.Detect(text);
        if (dialect is null)
        {
            return Fail(ImportErrorCodes.UnsupportedDelimiter, "Use a comma or a semicolon as the separator.");
        }

        IReadOnlyList<CsvRecord> records;
        try
        {
            records = CsvReader.Read(content, dialect.Delimiter);
        }
        catch (CsvFormatException exception)
        {
            return Fail(ImportErrorCodes.MalformedCsv, exception.Message);
        }

        var nonBlank = records.Where(record => !record.IsBlank).ToList();
        if (nonBlank.Count == 0)
        {
            return Fail(ImportErrorCodes.EmptyFile, "The file has no header row.");
        }

        var header = nonBlank[0].Cells.Select(name => name.Trim().ToLowerInvariant()).ToList();
        var repeated = header.Where(name => name.Length > 0).GroupBy(name => name).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToList();
        if (repeated.Count > 0)
        {
            return Fail(ImportErrorCodes.DuplicateColumns, $"Columns appear more than once: {string.Join(", ", repeated)}.");
        }

        var missing = BankCsvFormat.RequiredColumns.Where(column => !header.Contains(column)).ToList();
        if (missing.Count > 0)
        {
            return Fail(ImportErrorCodes.MissingColumns, $"Missing required columns: {string.Join(", ", missing)}.");
        }

        var dataRows = nonBlank.Skip(1).ToList();
        if (dataRows.Count > BankCsvFormat.MaxRows)
        {
            return (null, new ImportFileError(
                ImportErrorCodes.TooManyRows, $"The file has more than {BankCsvFormat.MaxRows} rows.", TooLarge: true));
        }

        var columns = header.Select((name, index) => (name, index)).Where(column => column.name.Length > 0)
            .ToDictionary(column => column.name, column => column.index);
        var rows = dataRows.Select(record => ValidateRow(record, header.Count, columns, dialect, today, categories)).ToList();
        var ignored = header
            .Where(name => name.Length > 0 && !BankCsvFormat.RequiredColumns.Contains(name) && !BankCsvFormat.OptionalColumns.Contains(name))
            .ToList();
        return (new TransactionImportPreview(rows, TransactionImportPreview.Summarize(rows), ignored), null);
    }

    private static TransactionImportRow ValidateRow(
        CsvRecord record,
        int columnCount,
        IReadOnlyDictionary<string, int> columns,
        CsvDialect dialect,
        DateOnly today,
        TransactionImportCategories categories)
    {
        if (record.Cells.Count != columnCount)
        {
            return Invalid(record, [new ImportError("row", ImportErrorCodes.WrongCellCount,
                $"The row has {record.Cells.Count} cells but the header has {columnCount}.")]);
        }

        string Cell(string column) => columns.TryGetValue(column, out var index) ? Clean(record.Cells[index]) : string.Empty;

        var errors = new List<ImportError>();
        var date = Date(Cell(BankCsvFormat.Date), dialect, today, errors);
        var amount = Amount(Cell(BankCsvFormat.Amount), dialect, errors);
        var description = Cell(BankCsvFormat.Description);
        if (description.Length > Transaction.DescriptionMaxLength)
        {
            errors.Add(new ImportError(BankCsvFormat.Description, ImportErrorCodes.TooLong,
                $"Use at most {Transaction.DescriptionMaxLength} characters."));
        }

        if (errors.Count > 0)
        {
            return Invalid(record, errors);
        }

        var type = amount < 0m ? TransactionType.Expense : TransactionType.Income;
        var kind = type == TransactionType.Expense ? CategoryKind.Expense : CategoryKind.Income;
        var categoryName = Cell(BankCsvFormat.Category);
        var match = categoryName.Length > 0 ? categories.Find(kind, categoryName) : null;
        var category = match ?? (kind == CategoryKind.Expense ? categories.DefaultExpense : categories.DefaultIncome);

        var values = new TransactionImportValues(
            date!.Value,
            type.ToString(),
            Math.Abs(amount!.Value),
            description.Length == 0 ? null : description,
            category.Id,
            category.Name,
            CategoryFallback: categoryName.Length > 0 && match is null);
        return new TransactionImportRow(record.RowNumber, ImportRowStatus.Valid, values, []);
    }

    // Quita el apóstrofo que protege contra fórmulas en hojas de cálculo y recorta espacios.
    private static string Clean(string cell)
    {
        var unguarded = cell.Length > 1 && cell[0] == SubscriptionCsvFormat.FormulaGuard
            && SubscriptionCsvFormat.FormulaPrefixes.Contains(cell[1])
                ? cell[1..]
                : cell;
        return unguarded.Trim();
    }

    private static DateOnly? Date(string value, CsvDialect dialect, DateOnly today, List<ImportError> errors)
    {
        if (value.Length == 0)
        {
            errors.Add(new ImportError(BankCsvFormat.Date, ImportErrorCodes.Required, "The date is required."));
            return null;
        }

        if (!DateOnly.TryParseExact(value, dialect.DateFormats.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add(new ImportError(BankCsvFormat.Date, ImportErrorCodes.InvalidDate,
                dialect.IsSpreadsheetLocale ? "Use a real date in yyyy-MM-dd or dd/MM/yyyy format." : "Use a real date in yyyy-MM-dd format."));
            return null;
        }

        if (date > today)
        {
            errors.Add(new ImportError(BankCsvFormat.Date, TransactionImportErrorCodes.DateInFuture, "The date cannot be in the future."));
            return null;
        }

        return date;
    }

    private static decimal? Amount(string value, CsvDialect dialect, List<ImportError> errors)
    {
        if (value.Length == 0)
        {
            errors.Add(new ImportError(BankCsvFormat.Amount, ImportErrorCodes.Required, "The amount is required."));
            return null;
        }

        var pattern = dialect.IsSpreadsheetLocale ? SpreadsheetAmountPattern() : AmountPattern();
        if (!pattern.IsMatch(value))
        {
            errors.Add(new ImportError(BankCsvFormat.Amount, ImportErrorCodes.InvalidAmount,
                "Use digits with an optional minus sign and up to 2 decimals, without thousands separators or currency symbols."));
            return null;
        }

        var amount = decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
        if (amount == 0m)
        {
            errors.Add(new ImportError(BankCsvFormat.Amount, TransactionImportErrorCodes.AmountZero, "The amount cannot be zero."));
            return null;
        }

        if (!Money.IsWithinRange(amount))
        {
            errors.Add(new ImportError(BankCsvFormat.Amount, ImportErrorCodes.InvalidAmount, "The amount is too large."));
            return null;
        }

        return amount;
    }

    private static TransactionImportRow Invalid(CsvRecord record, IReadOnlyList<ImportError> errors) =>
        new(record.RowNumber, ImportRowStatus.Invalid, null, errors);

    private static (TransactionImportPreview?, ImportFileError?) Fail(string code, string detail) =>
        (null, new ImportFileError(code, detail));
}

/// <summary>
/// Duplicados (EP-16 D6): una fila válida es duplicada si la cuenta ya tiene un movimiento con la misma fecha, tipo,
/// importe y descripción (sin distinguir mayúsculas ni espacios) o si coincide con una fila válida anterior del archivo.
/// </summary>
public static class BankCsvDuplicates
{
    public static TransactionImportPreview Classify(TransactionImportPreview preview, IEnumerable<Transaction> existing)
    {
        var known = existing
            .Where(t => t.Type != TransactionType.Transfer)
            .Select(t => Key(t.Date, t.Type.ToString(), t.Amount, t.Description))
            .ToHashSet();
        var seen = new Dictionary<(DateOnly, string, decimal, string), int>();

        var rows = preview.Rows.Select(row =>
        {
            if (row.Status != ImportRowStatus.Valid)
            {
                return row;
            }

            var values = row.Values!;
            var key = Key(values.Date, values.Type, values.Amount, values.Description);
            if (known.Contains(key))
            {
                return row with { Status = ImportRowStatus.Duplicate, DuplicateOf = ImportDuplicateSource.Existing };
            }

            if (seen.TryGetValue(key, out var earlier))
            {
                return row with { Status = ImportRowStatus.Duplicate, DuplicateOf = ImportDuplicateSource.File, DuplicateOfRow = earlier };
            }

            seen[key] = row.RowNumber;
            return row;
        }).ToList();

        return preview with { Rows = rows, Summary = TransactionImportPreview.Summarize(rows) };
    }

    private static (DateOnly, string, decimal, string) Key(DateOnly date, string type, decimal amount, string? description) =>
        (date, type, amount, (description ?? string.Empty).Trim().ToLowerInvariant());
}
