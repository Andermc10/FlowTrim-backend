using FlowTrim.Application.Abstractions;

namespace FlowTrim.Application.Accounts;

/// <summary>Caso de uso: obtener una cuenta por id. Devuelve <c>null</c> si no existe o es de otro propietario.</summary>
public interface IGetAccountHandler
{
    Task<AccountResponse?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GetAccountHandler : IGetAccountHandler
{
    private readonly IAccountRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetAccountHandler(IAccountRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<AccountResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var account = await _repository.GetByIdAsync(ownerId, id, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var activity = await _repository.GetActivityAsync(ownerId, id, cancellationToken);
        return AccountMapper.ToResponse(account, activity.GetValueOrDefault(id));
    }
}
