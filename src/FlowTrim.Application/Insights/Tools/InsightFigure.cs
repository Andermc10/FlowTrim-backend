using System.Text.Json.Serialization;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>
/// Cifra determinista que respalda una respuesta de Insights (HU-35). La arma el backend a partir del resultado
/// tipado de una herramienta; nunca sale del texto del modelo. <c>Kind</c> es una clave estable para traducir en la UI.
/// </summary>
public sealed record InsightFigure(
    string Kind,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency);

/// <summary>Claves estables de <see cref="InsightFigure.Kind"/> (contrato con HU-36).</summary>
public static class InsightFigureKinds
{
    public const string MonthlyEquivalent = "monthly_equivalent";
    public const string AnnualProjection = "annual_projection";
    public const string MonthlyTotal = "monthly_total";
    public const string CategorySpend = "category_spend";
    public const string UpcomingCharge = "upcoming_charge";
    public const string SelectedMonthly = "selected_monthly";
    public const string Savings = "savings";
    public const string PeriodCost = "period_cost";
    public const string DeclaredUses = "declared_uses";
    public const string CostPerUse = "cost_per_use";
}
