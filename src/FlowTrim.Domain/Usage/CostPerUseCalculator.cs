using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Usage;

/// <summary>Resultado determinista del cálculo de costo por uso.</summary>
public sealed record CostPerUseCalculation(
    DateOnly? From,
    DateOnly? To,
    int MonthsInPeriod,
    decimal TotalPaid,
    long TotalUses,
    bool Calculable,
    decimal? CostPerUse,
    string? Reason);

/// <summary>Calcula el costo declarado por uso sin dividir cuando no hay usos.</summary>
public static class CostPerUseCalculator
{
    private const string NoUsageReason = "No usage in the selected period.";

    /// <summary>
    /// Calcula el costo del periodo usando los registros ya filtrados al rango solicitado. Cuando no
    /// se especifica un límite del periodo, lo deriva de las fechas de los registros.
    /// </summary>
    public static CostPerUseCalculation Calculate(
        Subscription subscription,
        IReadOnlyList<UsageRecord> records,
        DateOnly? from,
        DateOnly? to)
    {
        DateOnly? periodFrom = from ?? records.MinBy(record => record.UsageDate)?.UsageDate;
        DateOnly? periodTo = to ?? records.MaxBy(record => record.UsageDate)?.UsageDate;

        if (periodFrom is null || periodTo is null || periodFrom > periodTo)
        {
            return new CostPerUseCalculation(
                periodFrom,
                periodTo,
                0,
                0m,
                0,
                false,
                null,
                NoUsageReason);
        }

        int monthsInPeriod = GetMonthsInPeriod(periodFrom.Value, periodTo.Value);
        decimal totalPaid = Math.Round(
            subscription.GetMonthlyEquivalent() * monthsInPeriod,
            2,
            MidpointRounding.AwayFromZero);
        long totalUses = records.Sum(record => (long)record.UsageCount);

        if (records.Count == 0 || totalUses == 0)
        {
            return new CostPerUseCalculation(
                periodFrom,
                periodTo,
                monthsInPeriod,
                totalPaid,
                totalUses,
                false,
                null,
                NoUsageReason);
        }

        decimal costPerUse = Math.Round(totalPaid / totalUses, 2, MidpointRounding.AwayFromZero);
        return new CostPerUseCalculation(
            periodFrom,
            periodTo,
            monthsInPeriod,
            totalPaid,
            totalUses,
            true,
            costPerUse,
            null);
    }

    private static int GetMonthsInPeriod(DateOnly from, DateOnly to) =>
        (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
}
