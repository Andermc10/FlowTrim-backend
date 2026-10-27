using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Budgets;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Dashboard;

/// <summary>Caso de uso: panel "Hoy" con "Puedes gastar hoy" por moneda (HU-68).</summary>
public interface IGetTodayDashboardHandler
{
    Task<TodayDashboardResponse> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reúne las entradas (saldos, totales del mes y de hoy sumados en SQL, suscripciones activas) y delega el cálculo en
/// <see cref="DailyAllowanceCalculator"/>, que es puro. "Hoy" y "este mes" salen del calendario del producto.
/// </summary>
public sealed class GetTodayDashboardHandler : IGetTodayDashboardHandler
{
    public const int RecentTransactionsCount = 5;

    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBudgetRepository _budgets;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetTodayDashboardHandler(
        IAccountRepository accounts,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        ISubscriptionRepository subscriptions,
        IBudgetRepository budgets,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _accounts = accounts;
        _categories = categories;
        _transactions = transactions;
        _subscriptions = subscriptions;
        _budgets = budgets;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<TodayDashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var today = ProductCalendar.Today(_timeProvider);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = DailyAllowanceCalculator.LastDayOfMonth(today);
        var none = new TransactionFilter(null, null, null, null, null, null);

        var accounts = await _accounts.ListAsync(ownerId, includeArchived: true, cancellationToken);
        var activity = await _accounts.GetActivityAsync(ownerId, null, cancellationToken);
        var monthTotals = await _transactions.GetTotalsAsync(ownerId, none with { From = monthStart, To = today }, cancellationToken);
        var todayTotals = await _transactions.GetTotalsAsync(ownerId, none with { From = today, To = today }, cancellationToken);
        var recent = await _transactions.ListAsync(ownerId, none, 0, RecentTransactionsCount, cancellationToken);
        var subscriptions = await _subscriptions.ListActiveAsync(ownerId, cancellationToken);
        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        var budgets = await BudgetOverview.BuildAsync(_budgets, _categories, _transactions, ownerId, monthStart, cancellationToken);

        var accountResponses = accounts
            .Select(account => AccountMapper.ToResponse(account, activity.GetValueOrDefault(account.Id)))
            .ToList();

        var currencies = DailyAllowanceCalculator.Calculate(
            today,
            accounts.Select(account => new AccountBalance(
                account.Type,
                account.Currency,
                AccountBalanceCalculator.GetBalance(account.OpeningBalance, activity.GetValueOrDefault(account.Id)),
                account.IsArchived)),
            monthTotals.Select(t => new PeriodTotals(t.Currency, t.Income, t.Expense)),
            todayTotals.Select(t => new PeriodTotals(t.Currency, t.Income, t.Expense)),
            subscriptions);

        // Un elemento por cobro esperado: un pago semanal aparece 4 o 5 veces (EP-14 D4).
        var charges = subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .SelectMany(s => DailyAllowanceCalculator.ChargesUntil(s, monthEnd).Select(date => (Subscription: s, Date: date)))
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Subscription.Name)
            .Select(c => new MonthChargeResponse(
                c.Subscription.Id, c.Subscription.Name, c.Subscription.Amount, c.Subscription.Currency, c.Date, c.Date < today))
            .ToList();

        return new TodayDashboardResponse(
            today,
            currencies.Select(c => new CurrencyTodayResponse(
                c.Currency, c.Available, c.MonthIncome, c.MonthExpenses, c.MonthNet, c.TodayExpenses,
                c.CommittedUntilMonthEnd, c.DaysLeft, c.SafeToSpendToday, c.Shortfall)).ToList(),
            accountResponses.Where(a => !a.IsArchived).ToList(),
            recent.Select(transaction => TransactionMapper.ToResponse(transaction, references)).ToList(),
            charges,
            BudgetOverview.ToWatch(budgets));
    }
}
