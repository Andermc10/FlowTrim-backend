using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Accounts;

namespace FlowTrim.Application.Accounts;

/// <summary>Caso de uso: editar una cuenta. La moneda queda fija en cuanto la cuenta tiene movimientos.</summary>
public interface IUpdateAccountHandler
{
    Task<HandlerResult<AccountResponse>> HandleAsync(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateAccountHandler : IUpdateAccountHandler
{
    public const string CurrencyLockedCode = "account_currency_locked";

    private readonly IAccountRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateAccountHandler(IAccountRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<AccountResponse>> HandleAsync(
        Guid id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var account = await _repository.GetByIdAsync(ownerId, id, cancellationToken);
        if (account is null)
        {
            return HandlerResult<AccountResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var type = EnumField.Parse<AccountType>(request.Type, "type", "Account type", errors);
        AccountRequestValidator.RequireOpeningBalance(request.OpeningBalance, errors);
        if (request.IsArchived is null)
        {
            errors["isArchived"] = ["Archived flag is required."];
        }

        await AccountRequestValidator.CheckUniqueNameAsync(_repository, ownerId, request.Name, id, errors, cancellationToken);

        if (errors.Count > 0)
        {
            return HandlerResult<AccountResponse>.Invalid(errors);
        }

        // Cambiar la moneda de una cuenta con movimientos cambiaría la moneda de todo su historial.
        var activity = (await _repository.GetActivityAsync(ownerId, id, cancellationToken)).GetValueOrDefault(id);
        if (activity is not null && Account.NormalizeCurrency(request.Currency) != account.Currency)
        {
            return HandlerResult<AccountResponse>.Conflict(
                CurrencyLockedCode, "The currency cannot change because the account has transactions.");
        }

        account.Update(
            request.Name,
            type,
            request.Currency,
            request.OpeningBalance!.Value,
            request.IsArchived!.Value,
            _timeProvider);

        await _repository.UpdateAsync(account, cancellationToken);
        return HandlerResult<AccountResponse>.Success(AccountMapper.ToResponse(account, activity));
    }
}
