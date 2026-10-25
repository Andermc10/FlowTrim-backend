using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Dashboard;

/// <summary>Saldo calculado de una cuenta, como entrada del cálculo (el saldo ya sale de <see cref="AccountBalanceCalculator"/>).</summary>
public sealed record AccountBalance(AccountType Type, string Currency, decimal Balance, bool IsArchived);

/// <summary>Ingresos y gastos de un periodo en una moneda (las transferencias ya están excluidas).</summary>
public sealed record PeriodTotals(string Currency, decimal Income, decimal Expense);

/// <summary>
/// Resultado de "Puedes gastar hoy" para una moneda (HU-68). Todas las entradas viajan con el resultado para que la
/// interfaz muestre la fórmula completa.
/// </summary>
public sealed record CurrencyAllowance(
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
/// "Puedes gastar hoy" por moneda (EP-12 §5, D7). Función pura y determinista, sin IA ni E/S:
/// <list type="bullet">
/// <item>disponible = Σ saldos de cuentas no archivadas de efectivo, banco y ahorros (las tarjetas son dinero prestado);</item>
/// <item>comprometido = Σ importes de los cobros esperados de los pagos activos hasta fin de mes, vencidos y hoy incluidos,
/// proyectados con <see cref="RecurrenceSchedule"/> (un pago semanal cuenta 4 o 5 veces, EP-14 D4);</item>
/// <item>días restantes = último día del mes − hoy + 1 (hoy incluido, siempre ≥ 1);</item>
/// <item>puedes gastar hoy = max(0, (disponible − comprometido) / días restantes), a 2 decimales AwayFromZero;</item>
/// <item>faltante = max(0, comprometido − disponible).</item>
/// </list>
/// Es una estimación: supone que no hay otros ingresos ni gastos en el mes. Las monedas nunca se suman ni convierten.
/// </summary>
public static class DailyAllowanceCalculator
{
    public static IReadOnlyList<CurrencyAllowance> Calculate(
        DateOnly today,
        IEnumerable<AccountBalance> accounts,
        IEnumerable<PeriodTotals> monthTotals,
        IEnumerable<PeriodTotals> todayTotals,
        IEnumerable<Subscription> subscriptions)
    {
        var accountList = accounts.ToList();
        var month = monthTotals.ToDictionary(t => t.Currency);
        var day = todayTotals.ToDictionary(t => t.Currency);
        var monthEnd = LastDayOfMonth(today);
        var committed = subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.NextBillingDate <= monthEnd)
            .GroupBy(s => s.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount * CountUntil(s, monthEnd)));

        // Una moneda aparece si tiene cuentas activas (aunque no tenga movimientos, AC-3), movimientos este mes o
        // cobros comprometidos (p. ej. suscripciones en USD sin cuentas en USD: disponible 0 y faltante visible).
        var currencies = accountList.Where(a => !a.IsArchived).Select(a => a.Currency)
            .Concat(month.Keys)
            .Concat(committed.Keys)
            .Distinct()
            .Order(StringComparer.Ordinal);

        var daysLeft = DaysLeft(today);
        return currencies.Select(currency =>
        {
            var available = accountList
                .Where(a => a.Currency == currency && !a.IsArchived && CountsAsAvailable(a.Type))
                .Sum(a => a.Balance);
            var monthIncome = month.GetValueOrDefault(currency)?.Income ?? 0m;
            var monthExpenses = month.GetValueOrDefault(currency)?.Expense ?? 0m;
            var charges = committed.GetValueOrDefault(currency);
            var safe = Math.Max(0m, Math.Round((available - charges) / daysLeft, 2, MidpointRounding.AwayFromZero));
            return new CurrencyAllowance(
                currency,
                available,
                monthIncome,
                monthExpenses,
                monthIncome - monthExpenses,
                day.GetValueOrDefault(currency)?.Expense ?? 0m,
                charges,
                daysLeft,
                safe,
                Math.Max(0m, charges - available));
        }).ToList();
    }

    /// <summary>
    /// Cobros esperados de un pago desde su próxima fecha (los vencidos cuentan una vez cada uno) hasta
    /// <paramref name="until"/> incluido.
    /// </summary>
    public static IReadOnlyList<DateOnly> ChargesUntil(Subscription subscription, DateOnly until) =>
        subscription.GetOccurrences(subscription.NextBillingDate, until);

    private static int CountUntil(Subscription subscription, DateOnly until) => ChargesUntil(subscription, until).Count;

    /// <summary>Días que quedan del mes contando hoy (el último día del mes da 1).</summary>
    public static int DaysLeft(DateOnly today) => LastDayOfMonth(today).DayNumber - today.DayNumber + 1;

    /// <summary>Último día del mes de la fecha (28, 29, 30 o 31).</summary>
    public static DateOnly LastDayOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    // Las tarjetas de crédito no son dinero propio: su saldo no se suma al disponible.
    private static bool CountsAsAvailable(AccountType type) => type is AccountType.Cash or AccountType.Bank or AccountType.Savings;
}
