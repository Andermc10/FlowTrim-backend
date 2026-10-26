using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Transactions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: últimos gastos registrados al marcar como pagado un pago recurrente (EP-14 HU-73 FR-3).</summary>
public interface IListSubscriptionPaymentsHandler
{
    /// <summary>Los últimos movimientos ligados al pago, o <c>null</c> si el pago no existe o es de otro propietario.</summary>
    Task<IReadOnlyList<TransactionResponse>?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ListSubscriptionPaymentsHandler : IListSubscriptionPaymentsHandler
{
    public const int Limit = 10;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly ITransactionRepository _transactions;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;

    public ListSubscriptionPaymentsHandler(
        ISubscriptionRepository subscriptions,
        ITransactionRepository transactions,
        IAccountRepository accounts,
        ICategoryRepository categories,
        ICurrentUser currentUser)
    {
        _subscriptions = subscriptions;
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TransactionResponse>?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        if (await _subscriptions.GetByIdAsync(ownerId, id, cancellationToken) is null)
        {
            return null;
        }

        var filter = new TransactionFilter(null, null, null, null, null, null, id);
        var payments = await _transactions.ListAsync(ownerId, filter, 0, Limit, cancellationToken);
        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        return payments.Select(payment => TransactionMapper.ToResponse(payment, references)).ToList();
    }
}
