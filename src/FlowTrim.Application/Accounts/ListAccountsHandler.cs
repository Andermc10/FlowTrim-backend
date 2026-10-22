using FlowTrim.Application.Abstractions;

namespace FlowTrim.Application.Accounts;

/// <summary>Caso de uso: listar las cuentas del propietario con su saldo calculado.</summary>
public interface IListAccountsHandler
{
    Task<IReadOnlyList<AccountResponse>> HandleAsync(bool includeArchived, CancellationToken cancellationToken);
}

public sealed class ListAccountsHandler : IListAccountsHandler
{
    private readonly IAccountRepository _repository;
    private readonly ICurrentUser _currentUser;

    public ListAccountsHandler(IAccountRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<AccountResponse>> HandleAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var accounts = await _repository.ListAsync(ownerId, includeArchived, cancellationToken);
        var activity = await _repository.GetActivityAsync(ownerId, null, cancellationToken);
        return accounts
            .Select(account => AccountMapper.ToResponse(account, activity.GetValueOrDefault(account.Id)))
            .ToList();
    }
}
