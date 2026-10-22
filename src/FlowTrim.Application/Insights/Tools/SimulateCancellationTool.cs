using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Simulations;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>
/// Ahorro potencial si se cancelan suscripciones activas nombradas (envuelve HU-07). Los nombres se resuelven solo
/// entre las suscripciones activas del propietario; nunca se cancela ni persiste nada.
/// </summary>
public sealed class SimulateCancellationTool(
    ISimulateHandler handler,
    ISubscriptionRepository repository,
    ICurrentUser currentUser) : FinancialTool
{
    public const int MaxNames = 10;
    public const int MaxHorizons = 5;
    public const int MaxHorizonMonths = 120;

    public override string Name => "simulate_cancellation";

    protected override string Description =>
        "Potential savings per currency if the named active subscriptions were cancelled, for 1, 12 and 24 months " +
        "unless other horizons are given. Names that match no active subscription are listed in unmatchedNames.";

    protected override string ParametersJsonSchema => $$$"""
        {"type":"object","properties":{"subscriptionNames":{"type":"array","minItems":1,"maxItems":{{{MaxNames}}},"items":{"type":"string","maxLength":{{{Subscription.NameMaxLength}}}},"description":"Exact names of the subscriptions to simulate cancelling."},"horizons":{"type":"array","minItems":1,"maxItems":{{{MaxHorizons}}},"items":{"type":"integer","minimum":1,"maximum":{{{MaxHorizonMonths}}}},"description":"Horizons in months (default 1, 12 and 24)."}},"required":["subscriptionNames"],"additionalProperties":false}
        """;

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var names = arguments.RequiredStringArray("subscriptionNames", MaxNames, Subscription.NameMaxLength);
        var horizons = arguments.OptionalIntArray("horizons", 1, MaxHorizonMonths, MaxHorizons);

        var active = await repository.ListActiveAsync(currentUser.GetOwnerId(), cancellationToken);
        var matched = active.Where(subscription => names.Any(name => SameName(name, subscription.Name))).ToList();
        var unmatchedNames = names
            .Where(name => !active.Any(subscription => SameName(name, subscription.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = await handler.HandleAsync(
            new SimulationRequest(matched.Select(subscription => subscription.Id).ToList(), horizons),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return ToolResult.Invalid(Name, string.Join(" ", result.Errors.Values.SelectMany(errors => errors)));
        }

        var simulation = result.Value!;
        // Solo las monedas con algo seleccionado: los ceros no son evidencia y el modelo tendía a narrarlos ("0 USD").
        var affected = simulation.Currencies.Where(currency => currency.SelectedMonthly > 0).ToList();
        var figures = affected
            .SelectMany(currency => currency.Savings
                .Select(saving => new InsightFigure(
                    InsightFigureKinds.Savings, "Potential savings", null, saving.Amount, currency.Currency, Months: saving.Months))
                .Prepend(new InsightFigure(
                    InsightFigureKinds.SelectedMonthly, "Selected monthly spending", null, currency.SelectedMonthly, currency.Currency)))
            .ToList();

        var data = new
        {
            selectedNames = matched.Select(subscription => subscription.Name).ToList(),
            unmatchedNames,
            simulation.Horizons,
            currencies = affected,
        };
        var assumptions = new[]
        {
            ToolAssumptions.NothingCancelled, ToolAssumptions.ConstantPrices, ToolAssumptions.ActiveOnly, ToolAssumptions.NoConversion,
        };
        return Ok(data, assumptions, "simulator") with
        {
            Figures = figures,
        };
    }
}
