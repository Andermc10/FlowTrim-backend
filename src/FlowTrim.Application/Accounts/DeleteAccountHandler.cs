using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;

namespace FlowTrim.Application.Accounts;

/// <summary>Caso de uso: borrar una cuenta sin movimientos; con movimientos solo se puede archivar (EP-12 D5).</summary>
public interface IDeleteAccountHandler
{
    Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteAccountHandler : IDeleteAccountHandler
{
    public const string HasTransactionsCode = "account_has_transactions";

    private readonly IAccountRepository _repository;
    private readonly ICurrentUser _currentUser;

    public DeleteAccountHandler(IAccountRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken) switch
        {
            AccountDeleteOutcome.Deleted => HandlerResult<Done>.Success(Done.Value),
            AccountDeleteOutcome.HasTransactions => HandlerResult<Done>.Conflict(
                HasTransactionsCode, "The account has transactions. Archive it instead."),
            _ => HandlerResult<Done>.NotFound(),
        };
}
