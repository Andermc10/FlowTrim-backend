using FlowTrim.Application.Accounts;
using FlowTrim.Application.Budgets;
using FlowTrim.Application.Transactions;

namespace FlowTrim.Application.Dashboard;

/// <summary>
/// Panel "Hoy" (HU-68): fecha de referencia del calendario del producto, "Puedes gastar hoy" por moneda con todas sus
/// entradas, las cuentas activas, los últimos movimientos y los cobros esperados hasta fin de mes.
/// </summary>
public sealed record TodayDashboardResponse(
    DateOnly ReferenceDate,
    IReadOnlyList<CurrencyTodayResponse> Currencies,
    IReadOnlyList<AccountResponse> Accounts,
    IReadOnlyList<TransactionResponse> RecentTransactions,
    IReadOnlyList<MonthChargeResponse> ChargesUntilMonthEnd,
    IReadOnlyList<BudgetResponse> BudgetsToWatch);

/// <summary>Cifras de una moneda. Nunca se suman monedas distintas.</summary>
public sealed record CurrencyTodayResponse(
    string Currency,
    decimal Available,
    decimal MonthIncome,
    decimal MonthExpenses,
    decimal MonthNet,
    decimal TodayExpenses,
    decimal CommittedUntilMonthEnd,
    int DaysLeft,
    decimal SafeToSpendToday,
    decimal Shortfall);

/// <summary>
/// Cobro esperado (no un pago) de un pago activo hasta el último día del mes; vencido si ya pasó. Un pago con varios
/// cobros en el mes aparece una vez por cobro: <c>NextBillingDate</c> es la fecha de ese cobro (nombre conservado por
/// compatibilidad de la API).
/// </summary>
public sealed record MonthChargeResponse(
    Guid SubscriptionId,
    string Name,
    decimal Amount,
    string Currency,
    DateOnly NextBillingDate,
    bool IsOverdue);
