using FlowTrim.Application.Analytics;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Gasto por categoría, top 5 y cobros esperados por mes (envuelve HU-50).</summary>
public sealed class SpendingAnalyticsTool(IGetAnalyticsHandler handler) : FinancialTool
{
    public override string Name => "get_spending_analytics";

    protected override string Description =>
        "Per currency: monthly spending by category with its share, the top 5 subscriptions and the expected charges " +
        "for each of the next months.";

    protected override string ParametersJsonSchema => $$$"""
        {"type":"object","properties":{"months":{"type":"integer","minimum":1,"maximum":{{{GetAnalyticsHandler.MaxMonths}}},"description":"Months of expected charges to include (default {{{GetAnalyticsHandler.DefaultMonths}}})."}},"additionalProperties":false}
        """;

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var months = arguments.OptionalInt("months", 1, GetAnalyticsHandler.MaxMonths, GetAnalyticsHandler.DefaultMonths);
        var result = await handler.HandleAsync(months, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToolResult.Invalid(Name, string.Join(" ", result.Errors.Values.SelectMany(errors => errors)));
        }

        var analytics = result.Value!;
        // Sin ids: el modelo solo necesita nombres y cifras.
        var currencies = analytics.Currencies.Select(currency => new
        {
            currency.Currency,
            currency.MonthlyTotal,
            currency.ByCategory,
            TopSubscriptions = currency.TopSubscriptions.Select(top => new { top.Name, top.MonthlyEquivalent }).ToList(),
            currency.ExpectedCharges,
        }).ToList();

        return Ok(
            new { analytics.ReferenceMonth, months, currencies },
            [ToolAssumptions.Expected, ToolAssumptions.ConstantPrices, ToolAssumptions.ActiveOnly, ToolAssumptions.NoConversion],
            "analytics");
    }
}
