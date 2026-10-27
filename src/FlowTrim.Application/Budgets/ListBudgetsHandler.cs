using System.Globalization;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;

namespace FlowTrim.Application.Budgets;

/// <summary>Caso de uso: presupuestos con su avance en un mes (EP-15 HU-76; por defecto, el mes de hoy).</summary>
public interface IListBudgetsHandler
{
    Task<HandlerResult<BudgetListResponse>> HandleAsync(string? month, CancellationToken cancellationToken);
}

public sealed class ListBudgetsHandler : IListBudgetsHandler
{
    public const string MonthFormatMessage = "Month must use the yyyy-MM format.";

    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public ListBudgetsHandler(
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

    public async Task<HandlerResult<BudgetListResponse>> HandleAsync(string? month, CancellationToken cancellationToken)
    {
        var today = ProductCalendar.Today(_timeProvider);
        var first = new DateOnly(today.Year, today.Month, 1);
        if (!string.IsNullOrWhiteSpace(month)
            && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            return HandlerResult<BudgetListResponse>.Invalid(new Dictionary<string, string[]> { ["month"] = [MonthFormatMessage] });
        }

        var items = await BudgetOverview.BuildAsync(
            _budgets, _categories, _transactions, _currentUser.GetOwnerId(), first, cancellationToken);
        return HandlerResult<BudgetListResponse>.Success(
            new BudgetListResponse(first.ToString("yyyy-MM", CultureInfo.InvariantCulture), items));
    }
}
