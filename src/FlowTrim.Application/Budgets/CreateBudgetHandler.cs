using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Budgets;

/// <summary>Caso de uso: crear un presupuesto mensual (EP-15 HU-76).</summary>
public interface ICreateBudgetHandler
{
    Task<HandlerResult<BudgetResponse>> HandleAsync(CreateBudgetRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// El dominio valida el límite y la moneda; aquí se valida la categoría (del propietario, de gastos y no archivada) y que
/// no exista otro presupuesto para la misma categoría y moneda (409 <c>budget_exists</c>).
/// </summary>
public sealed class CreateBudgetHandler : ICreateBudgetHandler
{
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateBudgetHandler(
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

    public async Task<HandlerResult<BudgetResponse>> HandleAsync(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var errors = new Dictionary<string, string[]>();
        if (request.Limit is null)
        {
            errors["limit"] = ["The monthly limit is required."];
        }

        if (request.CategoryId is not { } categoryId || categoryId == Guid.Empty)
        {
            errors["categoryId"] = ["Category is required."];
        }
        else
        {
            var category = await _categories.GetByIdAsync(ownerId, categoryId, cancellationToken);
            if (category is null)
            {
                errors["categoryId"] = ["Category not found."];
            }
            else if (category.Kind != CategoryKind.Expense)
            {
                errors["categoryId"] = ["A budget needs an expense category."];
            }
            else if (category.IsArchived)
            {
                errors["categoryId"] = ["The category is archived. Restore it to budget it."];
            }
        }

        if (errors.Count > 0)
        {
            return HandlerResult<BudgetResponse>.Invalid(errors);
        }

        // La moneda y el límite los valida el dominio (DomainValidationException → 400).
        var budget = Budget.Create(ownerId, request.CategoryId!.Value, request.Currency, request.Limit!.Value, _timeProvider);
        if (await _budgets.ExistsAsync(ownerId, budget.CategoryId, budget.Currency, cancellationToken))
        {
            return HandlerResult<BudgetResponse>.Conflict(
                "budget_exists", "This category already has a budget in that currency. Edit it instead.");
        }

        await _budgets.AddAsync(budget, cancellationToken);
        return HandlerResult<BudgetResponse>.Success(await BudgetOverview.CurrentAsync(
            _budgets, _categories, _transactions, _timeProvider, ownerId, budget.Id, cancellationToken));
    }
}
