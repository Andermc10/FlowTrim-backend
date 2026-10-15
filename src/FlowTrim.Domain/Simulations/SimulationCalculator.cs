using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Simulations;

/// <summary>
/// Motor puro de simulación de cancelación. Dadas las suscripciones ACTIVAS, el conjunto de ids
/// seleccionados para cancelar y los horizontes en meses, calcula por moneda el gasto actual, el
/// seleccionado, el proyectado y el ahorro por horizonte. No persiste nada ni mezcla monedas.
/// </summary>
public static class SimulationCalculator
{
    public static IReadOnlyList<CurrencySimulation> Calculate(
        IEnumerable<Subscription> activeSubscriptions,
        ISet<Guid> selectedIds,
        IReadOnlyList<int> horizons)
    {
        return activeSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .GroupBy(s => s.Currency)
            .Select(group =>
            {
                decimal currentMonthly = group.Sum(s => s.GetMonthlyEquivalent());
                decimal selectedMonthly = group
                    .Where(s => selectedIds.Contains(s.Id))
                    .Sum(s => s.GetMonthlyEquivalent());

                var savings = horizons
                    .Select(months => new SavingsAtHorizon(months, selectedMonthly * months))
                    .ToList();

                return new CurrencySimulation(
                    group.Key,
                    currentMonthly,
                    selectedMonthly,
                    currentMonthly - selectedMonthly,
                    savings);
            })
            .OrderBy(result => result.Currency, StringComparer.Ordinal)
            .ToList();
    }
}
