using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;

namespace FlowTrim.Application.Budgets;

/// <summary>Caso de uso: cambiar el límite mensual de un presupuesto (EP-15 HU-76).</summary>
public interface IUpdateBudgetHandler
{
    Task<HandlerResult<BudgetResponse>> HandleAsync(Guid id, UpdateBudgetRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateBudgetHandler : IUpdateBudgetHandler
{
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateBudgetHandler(
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _budgets = budgets;
        _categories = categories;
        _transactions = transactions;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<BudgetResponse>> HandleAsync(
        Guid id, UpdateBudgetRequest request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var budget = await _budgets.GetByIdAsync(ownerId, id, cancellationToken);
        if (budget is null)
        {
            return HandlerResult<BudgetResponse>.NotFound();
        }

        if (request.Limit is null)
        {
            return HandlerResult<BudgetResponse>.Invalid(
                new Dictionary<string, string[]> { ["limit"] = ["The monthly limit is required."] });
        }

        // Un límite inválido lanza DomainValidationException → 400 (manejador global).
        budget.ChangeLimit(request.Limit.Value, _timeProvider);
        await _budgets.UpdateAsync(budget, cancellationToken);
        return HandlerResult<BudgetResponse>.Success(await BudgetOverview.CurrentAsync(
            _budgets, _categories, _transactions, _timeProvider, ownerId, budget.Id, cancellationToken));
    }
}
