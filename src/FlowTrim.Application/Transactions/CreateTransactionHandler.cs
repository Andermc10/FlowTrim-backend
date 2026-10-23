using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Transactions;

/// <summary>Caso de uso: registrar un ingreso, un gasto o una transferencia (HU-66).</summary>
public interface ICreateTransactionHandler
{
    Task<HandlerResult<TransactionResponse>> HandleAsync(TransactionRequest request, CancellationToken cancellationToken);
}

public sealed class CreateTransactionHandler : ICreateTransactionHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateTransactionHandler(
        ITransactionRepository transactions,
        IAccountRepository accounts,
        ICategoryRepository categories,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<TransactionResponse>> HandleAsync(
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        var type = TransactionRequestValidator.Validate(
            request, references, ProductCalendar.Today(_timeProvider), previous: null, errors);

        if (errors.Count > 0)
        {
            return HandlerResult<TransactionResponse>.Invalid(errors);
        }

        // La forma (destino, categoría, importe, textos) la valida el dominio → 400 vía el manejador global.
        var transaction = Transaction.Create(
            ownerId,
            type,
            request.AccountId!.Value,
            request.ToAccountId,
            request.CategoryId,
            request.Amount!.Value,
            request.Date!.Value,
            request.Description,
            request.Note,
            _timeProvider);

        await _transactions.AddAsync(transaction, cancellationToken);
        return HandlerResult<TransactionResponse>.Success(TransactionMapper.ToResponse(transaction, references));
    }
}
