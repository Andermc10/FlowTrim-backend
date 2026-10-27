namespace FlowTrim.Domain.Budgets;

/// <summary>Estado de un presupuesto en el mes (EP-15 D3).</summary>
public enum BudgetState
{
    OnTrack = 1,
    Warning = 2,
    Exceeded = 3
}

/// <summary>Avance de un presupuesto en un mes: lo gastado contra el límite.</summary>
public sealed record BudgetProgress(decimal Spent, decimal Remaining, decimal Over, decimal Percent, BudgetState State);

/// <summary>
/// Avance de un presupuesto (EP-15 §5). Función pura: porcentaje = gastado / límite × 100 a 1 decimal (AwayFromZero);
/// menos de 80 % va bien, desde 80 % es aviso y desde 100 % está excedido. Nunca divide por cero: el límite siempre es
/// mayor que cero (invariante de <see cref="Budget"/>).
/// </summary>
public static class BudgetCalculator
{
    public const decimal WarningPercent = 80m;
    public const decimal ExceededPercent = 100m;

    public static BudgetProgress Evaluate(decimal limit, decimal spent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var percent = Math.Round(spent / limit * 100m, 1, MidpointRounding.AwayFromZero);
        var state = spent >= limit
            ? BudgetState.Exceeded
            : percent >= WarningPercent ? BudgetState.Warning : BudgetState.OnTrack;
        return new BudgetProgress(spent, Math.Max(0m, limit - spent), Math.Max(0m, spent - limit), percent, state);
    }
}
