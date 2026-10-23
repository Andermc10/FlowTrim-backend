using FlowTrim.Application.Abstractions;

namespace FlowTrim.Application.Transactions;

/// <summary>Caso de uso: borrar un movimiento (borrado definitivo: es un registro del propio usuario). <c>false</c> si no existía.</summary>
public interface IDeleteTransactionHandler
{
    Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteTransactionHandler : IDeleteTransactionHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly ICurrentUser _currentUser;

    public DeleteTransactionHandler(ITransactionRepository transactions, ICurrentUser currentUser)
    {
        _transactions = transactions;
        _currentUser = currentUser;
    }

    public Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        _transactions.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken);
}
