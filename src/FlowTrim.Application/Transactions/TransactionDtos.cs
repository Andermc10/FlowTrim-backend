using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Transactions;

/// <summary>
/// Petición de creación o reemplazo (PUT) de un movimiento. La moneda no viaja: es la de la cuenta. El tipo llega como
/// texto ("Expense") para reportar valores no admitidos con un 400 por campo.
/// </summary>
public sealed record TransactionRequest(
    string? Type,
    Guid? AccountId,
    Guid? ToAccountId,
    Guid? CategoryId,
    decimal? Amount,
    DateOnly? Date,
    string? Description,
    string? Note);

/// <summary>Movimiento público con los nombres de su cuenta y categoría (archivadas incluidas).</summary>
public sealed record TransactionResponse(
    Guid Id,
    string Type,
    Guid AccountId,
    string AccountName,
    Guid? ToAccountId,
    string? ToAccountName,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    string? CategoryIcon,
    decimal Amount,
    string Currency,
    DateOnly Date,
    string? Description,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Página del listado con los totales por moneda de todo el filtro (no solo de la página).</summary>
public sealed record TransactionListResponse(
    IReadOnlyList<TransactionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CurrencyTotals> Totals);

/// <summary>Cuentas y categorías del propietario (archivadas incluidas) para validar y mostrar movimientos.</summary>
internal sealed record TransactionReferences(
    IReadOnlyDictionary<Guid, Account> Accounts,
    IReadOnlyDictionary<Guid, Category> Categories);

/// <summary>Mapeo explícito de la entidad al DTO público.</summary>
internal static class TransactionMapper
{
    public static TransactionResponse ToResponse(Transaction transaction, TransactionReferences references)
    {
        var account = references.Accounts[transaction.AccountId];
        var toAccount = transaction.ToAccountId is { } toId ? references.Accounts[toId] : null;
        var category = transaction.CategoryId is { } categoryId ? references.Categories[categoryId] : null;
        return new TransactionResponse(
            transaction.Id,
            transaction.Type.ToString(),
            account.Id,
            account.Name,
            toAccount?.Id,
            toAccount?.Name,
            category?.Id,
            category?.Name,
            category?.Color,
            category?.Icon,
            transaction.Amount,
            account.Currency,
            transaction.Date,
            transaction.Description,
            transaction.Note,
            transaction.CreatedAt,
            transaction.UpdatedAt);
    }
}
