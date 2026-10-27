using System.Globalization;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Statements;

namespace FlowTrim.Application.Statements;

/// <summary>Categoría del estado de cuenta con su nombre, color e ícono (archivadas incluidas).</summary>
public sealed record StatementCategoryResponse(
    Guid CategoryId, string Name, string Color, string Icon, decimal Amount, decimal Share, decimal PreviousAmount);

/// <summary>Estado de cuenta de una moneda (EP-16 HU-78).</summary>
public sealed record CurrencyStatementResponse(
    string Currency,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal? SavingsRate,
    decimal PreviousIncome,
    decimal PreviousExpense,
    decimal PreviousNet,
    decimal? ExpenseChange,
    IReadOnlyList<StatementCategoryResponse> TopCategories);

/// <summary>Estado de cuenta de un mes <c>yyyy-MM</c> comparado con el mes anterior.</summary>
public sealed record StatementResponse(string Month, string PreviousMonth, IReadOnlyList<CurrencyStatementResponse> Currencies);

/// <summary>Caso de uso: estado de cuenta mensual (por defecto, el mes de hoy).</summary>
public interface IGetStatementHandler
{
    Task<HandlerResult<StatementResponse>> HandleAsync(string? month, CancellationToken cancellationToken);
}

/// <summary>
/// Reúne los totales por moneda y los gastos por categoría del mes y del anterior (sumados en SQL) y delega en
/// <see cref="MonthlyStatementCalculator"/>, que es puro.
/// </summary>
public sealed class GetStatementHandler : IGetStatementHandler
{
    public const string MonthFormatMessage = "Month must use the yyyy-MM format.";

    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetStatementHandler(
        ITransactionRepository transactions, ICategoryRepository categories, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _transactions = transactions;
        _categories = categories;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<StatementResponse>> HandleAsync(string? month, CancellationToken cancellationToken)
    {
        var today = ProductCalendar.Today(_timeProvider);
        var first = new DateOnly(today.Year, today.Month, 1);
        if (!string.IsNullOrWhiteSpace(month)
            && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            return HandlerResult<StatementResponse>.Invalid(new Dictionary<string, string[]> { ["month"] = [MonthFormatMessage] });
        }

        var ownerId = _currentUser.GetOwnerId();
        var last = first.AddMonths(1).AddDays(-1);
        var previousFirst = first.AddMonths(-1);
        var previousLast = first.AddDays(-1);
        var none = new TransactionFilter(null, null, null, null, null, null);

        var current = await _transactions.GetTotalsAsync(ownerId, none with { From = first, To = last }, cancellationToken);
        var previous = await _transactions.GetTotalsAsync(ownerId, none with { From = previousFirst, To = previousLast }, cancellationToken);
        var currentCategories = await _transactions.GetExpensesByCategoryAsync(ownerId, first, last, cancellationToken);
        var previousCategories = await _transactions.GetExpensesByCategoryAsync(ownerId, previousFirst, previousLast, cancellationToken);
        var byId = (await _categories.ListAsync(ownerId, null, includeArchived: true, cancellationToken)).ToDictionary(c => c.Id);

        var statements = MonthlyStatementCalculator.Build(
            current.Select(t => new PeriodTotals(t.Currency, t.Income, t.Expense)),
            previous.Select(t => new PeriodTotals(t.Currency, t.Income, t.Expense)),
            currentCategories.Select(c => new CategorySpending(c.CategoryId, c.Currency, c.Amount)),
            previousCategories.Select(c => new CategorySpending(c.CategoryId, c.Currency, c.Amount)));

        return HandlerResult<StatementResponse>.Success(new StatementResponse(
            first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            previousFirst.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            statements.Select(statement => new CurrencyStatementResponse(
                statement.Currency,
                statement.Income,
                statement.Expense,
                statement.Net,
                statement.SavingsRate,
                statement.PreviousIncome,
                statement.PreviousExpense,
                statement.PreviousNet,
                statement.ExpenseChange,
                statement.TopCategories
                    .Where(c => byId.ContainsKey(c.CategoryId))
                    .Select(c =>
                    {
                        var category = byId[c.CategoryId];
                        return new StatementCategoryResponse(
                            c.CategoryId, category.Name, category.Color, category.Icon, c.Amount, c.Share, c.PreviousAmount);
                    })
                    .ToList())).ToList()));
    }
}
