namespace FlowTrim.Domain.Goals;

/// <summary>Cifras de una meta de ahorro; las de proyección son nulas cuando no aplican.</summary>
public sealed record GoalProgress(
    decimal Saved,
    decimal Remaining,
    decimal Percent,
    bool Completed,
    bool IsOverdue,
    int? MonthsLeft,
    decimal? RequiredPerMonth,
    decimal AveragePerMonth,
    DateOnly? EstimatedCompletion);

/// <summary>
/// Cifras de una meta (EP-15 §5, D7/D8). Función pura:
/// <list type="bullet">
/// <item>restante = max(0, objetivo − ahorrado); porcentaje = ahorrado / objetivo × 100 (1 decimal); completada si
/// ahorrado ≥ objetivo;</item>
/// <item>con fecha objetivo: meses restantes = meses del mes actual al de la fecha, ambos incluidos (mínimo 1; vencida →
/// 1) y requerido por mes = restante / meses restantes (2 decimales);</item>
/// <item>promedio mensual = aportes de los 3 meses completos anteriores / 3; si es positivo y no está completada, fecha
/// estimada = primer día del mes actual + ceil(restante / promedio) meses.</item>
/// </list>
/// Son estimaciones: suponen aportes iguales cada mes.
/// </summary>
public static class GoalCalculator
{
    public const int AverageMonths = 3;

    /// <param name="saved">Σ de todos los aportes de la meta.</param>
    /// <param name="recentContributions">Σ de los aportes de los 3 meses completos anteriores al mes de <paramref name="today"/>.</param>
    public static GoalProgress Evaluate(decimal target, DateOnly? targetDate, decimal saved, decimal recentContributions, DateOnly today)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);

        var remaining = Math.Max(0m, target - saved);
        var percent = Math.Round(saved / target * 100m, 1, MidpointRounding.AwayFromZero);
        var completed = saved >= target;
        var average = Math.Round(recentContributions / AverageMonths, 2, MidpointRounding.AwayFromZero);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        int? monthsLeft = null;
        decimal? required = null;
        var isOverdue = false;
        if (targetDate is { } date)
        {
            isOverdue = date < today && !completed;
            monthsLeft = Math.Max(1, MonthIndex(date) - MonthIndex(today) + 1);
            required = completed ? null : Math.Round(remaining / monthsLeft.Value, 2, MidpointRounding.AwayFromZero);
        }

        DateOnly? estimated = null;
        if (!completed && average > 0m)
        {
            estimated = currentMonth.AddMonths((int)Math.Ceiling(remaining / average));
        }

        return new GoalProgress(saved, remaining, percent, completed, isOverdue, monthsLeft, required, average, estimated);
    }

    /// <summary>Primer día y último día de los 3 meses completos anteriores al mes de <paramref name="today"/>.</summary>
    public static (DateOnly From, DateOnly To) AveragePeriod(DateOnly today)
    {
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        return (currentMonth.AddMonths(-AverageMonths), currentMonth.AddDays(-1));
    }

    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
}
