using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Imports;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.TransactionImports;

/// <summary>Caso de uso: vista previa de un CSV del banco (EP-16 HU-79). No guarda nada.</summary>
public interface IPreviewTransactionImportHandler
{
    Task<TransactionImportResult> HandleAsync(
        string? fileName, Stream content, TransactionImportChoices choices, CancellationToken cancellationToken);
}

/// <summary>Caso de uso: confirmar reenviando el mismo archivo y elecciones (EP-16 D5): todo o nada.</summary>
public interface IConfirmTransactionImportHandler
{
    Task<TransactionImportResult> HandleAsync(
        string? fileName, Stream content, TransactionImportChoices choices, CancellationToken cancellationToken);
}

/// <summary>
/// Lo común a la vista previa y la confirmación: validar la cuenta y las categorías por defecto (del propietario,
/// activas y de la clase correcta; una ajena responde igual que una inexistente) y leer el archivo.
/// </summary>
internal sealed class TransactionImportReader(IAccountRepository accounts, ICategoryRepository categories, TimeProvider timeProvider)
{
    public async Task<(TransactionImportPreview? Preview, TransactionImportResult? Failure)> ReadAsync(
        string ownerId, string? fileName, Stream content, TransactionImportChoices choices, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (choices.AccountId is not { } accountId
            || await accounts.GetByIdAsync(ownerId, accountId, cancellationToken) is not { } account)
        {
            errors["accountId"] = ["Choose one of your accounts."];
        }
        else if (account.IsArchived)
        {
            errors["accountId"] = ["The account is archived. Restore it to import into it."];
        }

        var active = (await categories.ListAsync(ownerId, null, includeArchived: false, cancellationToken)).ToList();
        var defaultExpense = Pick(active, choices.ExpenseCategoryId, CategoryKind.Expense, "expenseCategoryId", errors);
        var defaultIncome = Pick(active, choices.IncomeCategoryId, CategoryKind.Income, "incomeCategoryId", errors);
        if (errors.Count > 0)
        {
            return (null, new TransactionImportResult { FieldErrors = errors });
        }

        var (text, fileError) = await CsvUpload.ReadTextAsync(fileName, content, cancellationToken);
        if (fileError is not null)
        {
            return (null, new TransactionImportResult { FileError = fileError });
        }

        var (preview, parseError) = BankCsvImport.Parse(
            text!, ProductCalendar.Today(timeProvider), new TransactionImportCategories(active, defaultExpense!, defaultIncome!));
        return parseError is not null ? (null, new TransactionImportResult { FileError = parseError }) : (preview, null);
    }

    private static Category? Pick(
        IReadOnlyList<Category> active, Guid? id, CategoryKind kind, string field, IDictionary<string, string[]> errors)
    {
        var category = active.FirstOrDefault(c => c.Id == id && c.Kind == kind);
        if (category is null)
        {
            errors[field] = [kind == CategoryKind.Expense
                ? "Choose one of your active expense categories."
                : "Choose one of your active income categories."];
        }

        return category;
    }

    /// <summary>Rango de fechas de las filas válidas, para buscar duplicados solo donde pueden estar.</summary>
    public static (DateOnly From, DateOnly To)? DateRange(TransactionImportPreview preview)
    {
        var dates = preview.Rows.Where(row => row.Values is not null).Select(row => row.Values!.Date).ToList();
        return dates.Count == 0 ? null : (dates.Min(), dates.Max());
    }
}

public sealed class PreviewTransactionImportHandler : IPreviewTransactionImportHandler
{
    private readonly TransactionImportReader _reader;
    private readonly ITransactionRepository _transactions;
    private readonly ICurrentUser _currentUser;

    public PreviewTransactionImportHandler(
        IAccountRepository accounts,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _reader = new TransactionImportReader(accounts, categories, timeProvider);
        _transactions = transactions;
        _currentUser = currentUser;
    }

    public async Task<TransactionImportResult> HandleAsync(
        string? fileName, Stream content, TransactionImportChoices choices, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var (preview, failure) = await _reader.ReadAsync(ownerId, fileName, content, choices, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        IReadOnlyList<Transaction> existing = [];
        if (TransactionImportReader.DateRange(preview!) is { } range)
        {
            existing = await _transactions.ListForImportAsync(ownerId, choices.AccountId!.Value, range.From, range.To, cancellationToken);
        }

        return new TransactionImportResult { Preview = BankCsvDuplicates.Classify(preview!, existing) };
    }
}

/// <summary>
/// Repite la lectura y la clasificación de duplicados dentro de una transacción serializada por propietario y crea
/// solo las filas válidas; si algo falla no se crea ninguna.
/// </summary>
public sealed class ConfirmTransactionImportHandler : IConfirmTransactionImportHandler
{
    private readonly TransactionImportReader _reader;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public ConfirmTransactionImportHandler(
        IAccountRepository accounts,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _reader = new TransactionImportReader(accounts, categories, timeProvider);
        _transactions = transactions;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<TransactionImportResult> HandleAsync(
        string? fileName, Stream content, TransactionImportChoices choices, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var (preview, failure) = await _reader.ReadAsync(ownerId, fileName, content, choices, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var summary = preview!.Summary;
        if (summary.Valid > 0 && TransactionImportReader.DateRange(preview) is { } range)
        {
            var accountId = choices.AccountId!.Value;
            await _transactions.AddImportedAsync(ownerId, accountId, range.From, range.To, existing =>
            {
                var classified = BankCsvDuplicates.Classify(preview, existing);
                summary = classified.Summary;
                return classified.Rows
                    .Where(row => row.Status == ImportRowStatus.Valid)
                    .Select(row => Transaction.Create(
                        ownerId,
                        Enum.Parse<TransactionType>(row.Values!.Type),
                        accountId,
                        null,
                        row.Values.CategoryId,
                        row.Values.Amount,
                        row.Values.Date,
                        row.Values.Description,
                        null,
                        _timeProvider))
                    .ToList();
            }, cancellationToken);
        }

        return new TransactionImportResult
        {
            Confirmation = new TransactionImportConfirmation(summary.Total, summary.Valid, summary.Invalid, summary.Duplicate),
        };
    }
}
