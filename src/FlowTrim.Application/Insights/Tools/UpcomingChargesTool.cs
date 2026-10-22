using FlowTrim.Application.Subscriptions;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Próximos cobros dentro de una ventana de días, incluidos los vencidos (envuelve HU-04).</summary>
public sealed class UpcomingChargesTool(IGetUpcomingChargesHandler handler) : FinancialTool
{
    public const int DefaultWithinDays = 30;
    public const int MaxWithinDays = 60;
    public const int MaxItems = 20;

    public override string Name => "get_upcoming_charges";

    protected override string Description =>
        "Expected charges of active subscriptions due within the given number of days, overdue ones included, " +
        "ordered by date, with days until the charge and the alert window.";

    protected override string ParametersJsonSchema => $$$"""
        {"type":"object","properties":{"withinDays":{"type":"integer","minimum":1,"maximum":{{{MaxWithinDays}}},"description":"Window in days from today (default {{{DefaultWithinDays}}})."}},"additionalProperties":false}
        """;

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var withinDays = arguments.OptionalInt("withinDays", 1, MaxWithinDays, DefaultWithinDays);
        var upcoming = await handler.HandleAsync(cancellationToken);

        // Los vencidos tienen días negativos, así que siempre entran en la ventana.
        var inWindow = upcoming.Items.Where(item => item.DaysUntilCharge <= withinDays).ToList();
        var listed = inWindow.Take(MaxItems).ToList();
        var figures = listed.Select(item => new InsightFigure(
            InsightFigureKinds.UpcomingCharge, "Expected charge", item.Name, item.Amount, item.Currency, Date: item.NextBillingDate))
            .ToList();
        var charges = listed.Select(item => new
        {
            item.Name,
            item.Amount,
            item.Currency,
            item.BillingCycle,
            item.NextBillingDate,
            item.DaysUntilCharge,
            item.AlertWindow,
        }).ToList();

        return Ok(
            new { upcoming.ReferenceDate, withinDays, totalCount = inWindow.Count, charges },
            [ToolAssumptions.Expected, ToolAssumptions.ActiveOnly],
            "upcoming-charges") with
        { Figures = figures };
    }
}
