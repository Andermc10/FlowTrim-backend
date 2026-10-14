using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Summaries;

/// <summary>
/// Motor financiero puro (sin reloj ni dependencias): agrupa las suscripciones ACTIVAS por moneda
/// y calcula el equivalente mensual y la proyección anual. Nunca suma importes de monedas distintas.
/// </summary>
public static class SummaryCalculator
{
    public static IReadOnlyList<CurrencySummary> Calculate(IEnumerable<Subscription> subscriptions)
    {
        return subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .GroupBy(s => s.Currency)
            .Select(group =>
            {
                // Redondeo por suscripción (en GetMonthlyEquivalent) y luego suma; anual = mensual × 12.
                decimal monthlyEquivalent = group.Sum(s => s.GetMonthlyEquivalent());
                return new CurrencySummary(group.Key, monthlyEquivalent, monthlyEquivalent * 12m, group.Count());
            })
            .OrderBy(summary => summary.Currency, StringComparer.Ordinal)
            .ToList();
    }
}
