using FlowTrim.Application.Summaries;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Resumen mensual y proyección anual por moneda (envuelve HU-03).</summary>
public sealed class SpendingSummaryTool(IGetSummaryHandler handler) : FinancialTool
{
    public override string Name => "get_spending_summary";

    protected override string Description =>
        "Monthly-equivalent spending, annual projection and number of active subscriptions, per currency.";

    protected override string ParametersJsonSchema => """{"type":"object","properties":{},"additionalProperties":false}""";

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var summary = await handler.HandleAsync(cancellationToken);
        var figures = summary.Currencies.SelectMany(currency => new[]
        {
            new InsightFigure(InsightFigureKinds.MonthlyEquivalent, "Monthly equivalent", null, currency.MonthlyEquivalent, currency.Currency),
            new InsightFigure(InsightFigureKinds.AnnualProjection, "Annual projection", null, currency.AnnualProjection, currency.Currency),
        }).ToList();

        return Ok(
            new { currencies = summary.Currencies },
            [ToolAssumptions.Expected, ToolAssumptions.ConstantPrices, ToolAssumptions.ActiveOnly, ToolAssumptions.NoConversion],
            "summary") with
        { Figures = figures };
    }
}
