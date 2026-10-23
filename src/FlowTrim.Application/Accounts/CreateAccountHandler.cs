using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Accounts;

namespace FlowTrim.Application.Accounts;

/// <summary>Caso de uso: crear una cuenta.</summary>
public interface ICreateAccountHandler
{
    Task<HandlerResult<AccountResponse>> HandleAsync(CreateAccountRequest request, CancellationToken cancellationToken);
}

public sealed class CreateAccountHandler : ICreateAccountHandler
{
    private readonly IAccountRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateAccountHandler(IAccountRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<AccountResponse>> HandleAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var errors = new Dictionary<string, string[]>();
        var type = EnumField.Parse<AccountType>(request.Type, "type", "Account type", errors);
        AccountRequestValidator.RequireOpeningBalance(request.OpeningBalance, errors);
        await AccountRequestValidator.CheckUniqueNameAsync(_repository, ownerId, request.Name, null, errors, cancellationToken);

        if (errors.Count > 0)
        {
            return HandlerResult<AccountResponse>.Invalid(errors);
        }

        // Las invariantes de valor (nombre, moneda, saldo inicial) son del dominio → 400 vía el manejador global.
        var account = Account.Create(
            ownerId,
            request.Name,
            type,
            request.Currency,
            request.OpeningBalance!.Value,
            _timeProvider);

        await _repository.AddAsync(account, cancellationToken);
        return HandlerResult<AccountResponse>.Success(AccountMapper.ToResponse(account, null));
    }
}
