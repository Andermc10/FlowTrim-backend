using System.Text.Json.Serialization;
using FlowTrim.Application.Imports;
using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.TransactionImports;

/// <summary>Columnas del CSV de un banco (EP-16 D3): <c>date</c> y <c>amount</c> obligatorias; el resto opcionales.</summary>
public static class BankCsvFormat
{
    public const string Date = "date";
    public const string Amount = "amount";
    public const string Description = "description";
    public const string Category = "category";
    public const int MaxRows = 500;

    public static readonly IReadOnlyList<string> RequiredColumns = [Date, Amount];
    public static readonly IReadOnlyList<string> OptionalColumns = [Description, Category];
}

/// <summary>Códigos de error de fila propios de la importación de movimientos (los demás son los de EP-07).</summary>
public static class TransactionImportErrorCodes
{
    public const string DateInFuture = "date_in_future";
    public const string AmountZero = "amount_zero";
}

/// <summary>Valores normalizados de una fila: tipo por el signo del importe y la categoría que se usará.</summary>
public sealed record TransactionImportValues(
    DateOnly Date,
    string Type,
    decimal Amount,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    bool CategoryFallback);

/// <summary>Fila del archivo con su estado; una duplicada indica con qué coincidió.</summary>
public sealed record TransactionImportRow(
    int RowNumber,
    string Status,
    TransactionImportValues? Values,
    IReadOnlyList<ImportError> Errors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DuplicateOf = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DuplicateOfRow = null);

/// <summary>Vista previa de la importación (nada se ha guardado). <c>Summary.Valid</c> = filas que se crearían.</summary>
public sealed record TransactionImportPreview(
    IReadOnlyList<TransactionImportRow> Rows,
    ImportSummary Summary,
    IReadOnlyList<string> IgnoredColumns)
{
    public static ImportSummary Summarize(IReadOnlyCollection<TransactionImportRow> rows) => new(
        rows.Count,
        rows.Count(row => row.Status == ImportRowStatus.Valid),
        rows.Count(row => row.Status == ImportRowStatus.Invalid),
        rows.Count(row => row.Status == ImportRowStatus.Duplicate));
}

/// <summary>Conteos de una importación confirmada; siempre suman <c>Total</c>.</summary>
public sealed record TransactionImportConfirmation(int Total, int Created, int Invalid, int Duplicate);

/// <summary>Cuenta y categorías por defecto elegidas para el archivo (campos del formulario multipart).</summary>
public sealed record TransactionImportChoices(Guid? AccountId, Guid? ExpenseCategoryId, Guid? IncomeCategoryId);

/// <summary>Categorías activas del propietario para resolver la columna <c>category</c> y las de reserva.</summary>
public sealed record TransactionImportCategories(
    IReadOnlyList<Category> Active, Category DefaultExpense, Category DefaultIncome)
{
    /// <summary>La categoría activa de esa clase con ese nombre (sin distinguir mayúsculas), o <c>null</c>.</summary>
    public Category? Find(CategoryKind kind, string name) =>
        Active.FirstOrDefault(c => c.Kind == kind && string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Resultado de leer el archivo con las elecciones: la vista previa, un error de archivo o errores de campos.</summary>
public sealed class TransactionImportResult
{
    public TransactionImportPreview? Preview { get; init; }
    public ImportFileError? FileError { get; init; }
    public IDictionary<string, string[]>? FieldErrors { get; init; }
    public TransactionImportConfirmation? Confirmation { get; init; }
}
