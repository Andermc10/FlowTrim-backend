using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;

namespace FlowTrim.Application.Transactions;

/// <summary>Caso de uso: obtener un movimiento por id. Devuelve <c>null</c> si no existe o es de otro propietario.</summary>
public interface IGetTransactionHandler
{
    Task<TransactionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GetTransactionHandler : IGetTransactionHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;

    public GetTransactionHandler(
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

    public async Task<TransactionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var transaction = await _transactions.GetByIdAsync(ownerId, id, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        return TransactionMapper.ToResponse(transaction, references);
    }
}
