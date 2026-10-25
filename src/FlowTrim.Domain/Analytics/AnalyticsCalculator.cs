using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Analytics;

/// <summary>Gasto mensual equivalente de una categoría dentro de una moneda.</summary>
public sealed record CategorySpend(string Category, decimal MonthlyEquivalent, decimal Share, int Count);

/// <summary>Una de las suscripciones de mayor equivalente mensual.</summary>
public sealed record TopSubscription(Guid Id, string Name, decimal MonthlyEquivalent);

/// <summary>Cobros esperados (no confirmados) en un mes calendario.</summary>
public sealed record MonthlyCharge(int Year, int Month, decimal Amount, int Count);

/// <summary>Analítica de una moneda: nunca se mezclan importes de monedas distintas.</summary>
public sealed record CurrencyAnalytics(
    string Currency,
    decimal MonthlyTotal,
    IReadOnlyList<CategorySpend> ByCategory,
    IReadOnlyList<TopSubscription> TopSubscriptions,
    IReadOnlyList<MonthlyCharge> ExpectedCharges);

/// <summary>
/// Motor de analítica puro (sin reloj ni base de datos): agrupa las suscripciones ACTIVAS por moneda y
/// calcula gasto por categoría, las más caras y los cobros esperados mes a mes a partir de la fecha de
/// referencia. Reutiliza el equivalente mensual de HU-03.
/// </summary>
public static class AnalyticsCalculator
{
    public const string UncategorizedLabel = "Uncategorized";
    public const int TopSubscriptionsLimit = 5;

    public static IReadOnlyList<CurrencyAnalytics> Calculate(
        IEnumerable<Subscription> subscriptions, DateOnly referenceDate, int months)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);

        return subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .GroupBy(s => s.Currency)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CalculateCurrency(group.Key, group.ToList(), referenceDate, months))
            .ToList();
    }

    private static CurrencyAnalytics CalculateCurrency(
        string currency, IReadOnlyList<Subscription> subscriptions, DateOnly referenceDate, int months)
    {
        decimal monthlyTotal = subscriptions.Sum(s => s.GetMonthlyEquivalent());

        var byCategory = subscriptions
            .GroupBy(s => s.Category ?? UncategorizedLabel)
            .Select(group =>
            {
                decimal amount = group.Sum(s => s.GetMonthlyEquivalent());
                // Un plan anual de 0.05 o menos equivale a 0.00 al mes: el total puede ser cero y no se divide.
                decimal share = monthlyTotal == 0m
                    ? 0m
                    : Math.Round(amount / monthlyTotal * 100m, 1, MidpointRounding.AwayFromZero);
                return new CategorySpend(group.Key, amount, share, group.Count());
            })
            .OrderByDescending(c => c.MonthlyEquivalent)
            .ThenBy(c => c.Category, StringComparer.Ordinal)
            .ToList();

        var top = subscriptions
            .OrderByDescending(s => s.GetMonthlyEquivalent())
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Take(TopSubscriptionsLimit)
            .Select(s => new TopSubscription(s.Id, s.Name, s.GetMonthlyEquivalent()))
            .ToList();

        return new CurrencyAnalytics(currency, monthlyTotal, byCategory, top,
            ExpectedCharges(subscriptions, referenceDate, months));
    }

    /// <summary>
    /// Cobros esperados por mes desde el mes de referencia, proyectados con <see cref="RecurrenceSchedule"/> desde la
    /// próxima fecha de cada pago (EP-14 D4): un pago semanal suma 4 o 5 cobros en un mes y uno anual solo el mes de su
    /// aniversario. Los cobros vencidos de meses anteriores no se muestran. Se usa el importe real, no el equivalente.
    /// </summary>
    private static IReadOnlyList<MonthlyCharge> ExpectedCharges(
        IReadOnlyList<Subscription> subscriptions, DateOnly referenceDate, int months)
    {
        int referenceIndex = MonthIndex(referenceDate);
        var amounts = new decimal[months];
        var counts = new int[months];
        var firstDay = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
        var lastDay = firstDay.AddMonths(months).AddDays(-1);

        foreach (var subscription in subscriptions)
        {
            foreach (var date in subscription.GetOccurrences(firstDay, lastDay))
            {
                int offset = MonthIndex(date) - referenceIndex;
                amounts[offset] += subscription.Amount;
                counts[offset]++;
            }
        }

        return Enumerable.Range(0, months)
            .Select(offset =>
            {
                int index = referenceIndex + offset;
                return new MonthlyCharge(index / 12, index % 12 + 1, amounts[offset], counts[offset]);
            })
            .ToList();
    }

    // Índice absoluto de mes (año × 12 + mes − 1) para comparar y avanzar meses sin fechas.
    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
}
