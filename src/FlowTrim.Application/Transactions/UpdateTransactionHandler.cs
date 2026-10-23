using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;

namespace FlowTrim.Application.Transactions;

/// <summary>
/// Caso de uso: reemplazar un movimiento. Como los saldos se calculan, cambiar su cuenta o su tipo actualiza los dos
/// saldos afectados sin más trabajo.
/// </summary>
public interface IUpdateTransactionHandler
{
    Task<HandlerResult<TransactionResponse>> HandleAsync(Guid id, TransactionRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateTransactionHandler : IUpdateTransactionHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateTransactionHandler(
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
        Guid id,
        TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var transaction = await _transactions.GetByIdAsync(ownerId, id, cancellationToken);
        if (transaction is null)
        {
            return HandlerResult<TransactionResponse>.NotFound();
        }

        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        var type = TransactionRequestValidator.Validate(
            request, references, ProductCalendar.Today(_timeProvider), transaction, errors);

        if (errors.Count > 0)
        {
            return HandlerResult<TransactionResponse>.Invalid(errors);
        }

        transaction.Update(
            type,
            request.AccountId!.Value,
            request.ToAccountId,
            request.CategoryId,
            request.Amount!.Value,
            request.Date!.Value,
            request.Description,
            request.Note,
            _timeProvider);

        await _transactions.UpdateAsync(transaction, cancellationToken);
        return HandlerResult<TransactionResponse>.Success(TransactionMapper.ToResponse(transaction, references));
    }
}
