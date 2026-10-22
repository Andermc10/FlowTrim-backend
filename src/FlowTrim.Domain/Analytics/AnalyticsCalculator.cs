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
    /// Cobros esperados por mes desde el mes de referencia. Mensual: cada mes desde el mes de su próximo
    /// cobro (o desde el mes de referencia si está vencido). Anual: el mes de su próximo cobro y cada 12 meses;
    /// si ese mes ya pasó, su siguiente aniversario. Se usa el importe real cobrado, no el equivalente.
    /// </summary>
    private static IReadOnlyList<MonthlyCharge> ExpectedCharges(
        IReadOnlyList<Subscription> subscriptions, DateOnly referenceDate, int months)
    {
        int referenceIndex = MonthIndex(referenceDate);
        var amounts = new decimal[months];
        var counts = new int[months];

        foreach (var subscription in subscriptions)
        {
            int billingIndex = MonthIndex(subscription.NextBillingDate);
            for (int offset = 0; offset < months; offset++)
            {
                if (IsChargedIn(subscription.BillingCycle, billingIndex, referenceIndex + offset))
                {
                    amounts[offset] += subscription.Amount;
                    counts[offset]++;
                }
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

    // Nunca hay cobro antes del mes del próximo cobro; un cobro vencido ya queda antes del mes de referencia,
    // así que una mensual vencida se cobra en todos los meses mostrados y una anual en su siguiente aniversario.
    private static bool IsChargedIn(BillingCycle cycle, int billingIndex, int monthIndex) => cycle switch
    {
        BillingCycle.Monthly => monthIndex >= billingIndex,
        BillingCycle.Yearly => monthIndex >= billingIndex && (monthIndex - billingIndex) % 12 == 0,
        _ => throw new InvalidOperationException($"Unsupported billing cycle: {cycle}"),
    };

    // Índice absoluto de mes (año × 12 + mes − 1) para comparar y avanzar meses sin fechas.
    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
}
