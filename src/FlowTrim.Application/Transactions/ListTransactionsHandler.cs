using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Transactions;

/// <summary>Parámetros del listado tal como llegan por la query string (sin validar).</summary>
public sealed record ListTransactionsQuery(
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    string? Type,
    string? Search,
    int? Page,
    int? PageSize);

/// <summary>Caso de uso: listar movimientos filtrados y paginados, con totales por moneda de todo el filtro.</summary>
public interface IListTransactionsHandler
{
    Task<HandlerResult<TransactionListResponse>> HandleAsync(ListTransactionsQuery query, CancellationToken cancellationToken);
}

public sealed class ListTransactionsHandler : IListTransactionsHandler
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private readonly ITransactionRepository _transactions;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;

    public ListTransactionsHandler(
        ITransactionRepository transactions,
        IAccountRepository accounts,
        ICategoryRepository categories,
        ICurrentUser currentUser)
    {
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<TransactionListResponse>> HandleAsync(
        ListTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        TransactionType? type = query.Type is null
            ? null
            : EnumField.Parse<TransactionType>(query.Type, "type", "Transaction type", errors);
        if (query.From is { } from && query.To is { } to && from > to)
        {
            errors["from"] = ["'From' must be on or before 'to'."];
        }

        if (errors.Count > 0)
        {
            return HandlerResult<TransactionListResponse>.Invalid(errors);
        }

        // Saneado de parámetros fuera de rango, igual que el listado de suscripciones.
        var page = query.Page is null or < 1 ? 1 : query.Page.Value;
        var pageSize = query.PageSize is null or < 1 ? DefaultPageSize : Math.Min(query.PageSize.Value, MaxPageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var filter = new TransactionFilter(query.From, query.To, query.AccountId, query.CategoryId, type, search);

        var ownerId = _currentUser.GetOwnerId();
        var totalCount = await _transactions.CountAsync(ownerId, filter, cancellationToken);
        var items = await _transactions.ListAsync(ownerId, filter, (page - 1) * pageSize, pageSize, cancellationToken);
        var totals = await _transactions.GetTotalsAsync(ownerId, filter, cancellationToken);
        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);

        var response = new TransactionListResponse(
            items.Select(transaction => TransactionMapper.ToResponse(transaction, references)).ToList(),
            page,
            pageSize,
            totalCount,
            totals);
        return HandlerResult<TransactionListResponse>.Success(response);
    }
}
