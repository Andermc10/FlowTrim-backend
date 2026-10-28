using FlowTrim.Application.Budgets;
using FlowTrim.Application.Goals;
using FlowTrim.Application.Statements;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Ingresos, gastos, neto y categorías de un mes, comparados con el anterior (envuelve EP-16 HU-78).</summary>
public sealed class MonthSpendingTool(IGetStatementHandler handler) : FinancialTool
{
    public override string Name => "get_month_spending";

    protected override string Description =>
        "Per currency for one month (default: this month): recorded income, expenses, net, savings rate, the previous " +
        "month's figures, the expense change in percent and the top expense categories. Only transactions the user recorded.";

    protected override string ParametersJsonSchema =>
        """{"type":"object","properties":{"month":{"type":"string","pattern":"^\\d{4}-\\d{2}$","description":"Month in yyyy-MM format (default: the current month)."}},"additionalProperties":false}""";

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var month = arguments.OptionalString("month", 7);
        var result = await handler.HandleAsync(month, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToolResult.Invalid(Name, string.Join(" ", result.Errors.Values.SelectMany(errors => errors)));
        }

        var statement = result.Value!;
        var date = DateOnly.ParseExact(statement.Month + "-01", "yyyy-MM-dd");
        // Sin ids de categorías: el modelo solo necesita nombres y cifras.
        var currencies = statement.Currencies.Select(currency => new
        {
            currency.Currency,
            currency.Income,
            currency.Expense,
            currency.Net,
            currency.SavingsRate,
            currency.PreviousIncome,
            currency.PreviousExpense,
            currency.ExpenseChange,
            TopCategories = currency.TopCategories.Select(c => new { c.Name, c.Amount, c.Share, c.PreviousAmount }).ToList(),
        }).ToList();
        var figures = statement.Currencies.SelectMany(currency => new[]
        {
            new InsightFigure(InsightFigureKinds.MonthIncome, "Income", null, currency.Income, currency.Currency, Date: date),
            new InsightFigure(InsightFigureKinds.MonthExpense, "Expenses", null, currency.Expense, currency.Currency, Date: date),
            new InsightFigure(InsightFigureKinds.MonthNet, "Net", null, currency.Net, currency.Currency, Date: date),
        }).ToList();

        return Ok(
            new { statement.Month, statement.PreviousMonth, currencies },
            [ToolAssumptions.RecordedOnly, ToolAssumptions.NoConversion],
            "statement") with
        { Figures = figures };
    }
}

/// <summary>Presupuestos del mes actual: límite, gastado, restante y estado (envuelve EP-15 HU-76).</summary>
public sealed class BudgetsTool(IListBudgetsHandler handler) : FinancialTool
{
    public override string Name => "get_budgets";

    protected override string Description =>
        "This month's budgets per expense category and currency: limit, recorded spending, remaining, amount over the " +
        "limit, percent used and state (OnTrack, Warning from 80 %, Exceeded from 100 %).";

    protected override string ParametersJsonSchema => """{"type":"object","properties":{},"additionalProperties":false}""";

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(null, cancellationToken);
        var list = result.Value!;
        var budgets = list.Items.Select(b => new
        {
            Category = b.CategoryName, b.Currency, b.Limit, b.Spent, b.Remaining, b.Over, b.Percent, b.State,
        }).ToList();
        var figures = list.Items.SelectMany(b => new[]
        {
            new InsightFigure(InsightFigureKinds.BudgetSpent, "Spent", b.CategoryName, b.Spent, b.Currency),
            new InsightFigure(InsightFigureKinds.BudgetRemaining, "Remaining", b.CategoryName, b.Remaining, b.Currency),
        }).ToList();

        return Ok(new { list.Month, budgets }, [ToolAssumptions.RecordedOnly, ToolAssumptions.NoConversion], "budgets") with
        {
            Figures = figures,
        };
    }
}

/// <summary>Metas de ahorro: ahorrado, restante, necesario por mes y fecha estimada (envuelve EP-15 HU-77).</summary>
public sealed class GoalsTool(IListGoalsHandler handler) : FinancialTool
{
    public override string Name => "get_goals";

    protected override string Description =>
        "Savings goals: target, saved so far, remaining, percent, target date, amount needed per month to reach it, " +
        "average monthly contribution of the last 3 full months and the estimated completion month.";

    protected override string ParametersJsonSchema => """{"type":"object","properties":{},"additionalProperties":false}""";

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var goals = await handler.HandleAsync(cancellationToken);
        var data = goals.Select(g => new
        {
            g.Name, g.Currency, g.Target, g.Saved, g.Remaining, g.Percent, g.Completed, g.TargetDate, g.RequiredPerMonth,
            g.AveragePerMonth, g.EstimatedCompletion,
        }).ToList();
        var figures = goals.SelectMany(g =>
        {
            var items = new List<InsightFigure>
            {
                new(InsightFigureKinds.GoalSaved, "Saved", g.Name, g.Saved, g.Currency),
                new(InsightFigureKinds.GoalRemaining, "Remaining", g.Name, g.Remaining, g.Currency),
            };
            if (g.RequiredPerMonth is { } required)
            {
                items.Add(new InsightFigure(InsightFigureKinds.GoalRequiredPerMonth, "Needed per month", g.Name, required, g.Currency, Date: g.TargetDate));
            }

            return items;
        }).ToList();

        return Ok(new { goals = data }, [ToolAssumptions.DeclaredContributions, ToolAssumptions.NoConversion], "goals") with
        {
            Figures = figures,
        };
    }
}
