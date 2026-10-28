using System.Text.Json.Serialization;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>
/// Cifra determinista que respalda una respuesta de Insights (HU-35). La arma el backend a partir del resultado
/// tipado de una herramienta; nunca sale del texto del modelo. <c>Kind</c> es una clave estable para traducir en la UI.
/// <c>Subject</c> es solo un dato del usuario (nombre o categoría); el horizonte y la fecha van en campos tipados para
/// que la UI los formatee en su idioma.
/// </summary>
public sealed record InsightFigure(
    string Kind,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Months = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? Date = null);

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
    public const string MonthIncome = "month_income";
    public const string MonthExpense = "month_expense";
    public const string MonthNet = "month_net";
    public const string BudgetSpent = "budget_spent";
    public const string BudgetRemaining = "budget_remaining";
    public const string GoalSaved = "goal_saved";
    public const string GoalRemaining = "goal_remaining";
    public const string GoalRequiredPerMonth = "goal_required_per_month";
}
