using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Usage;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>
/// Costo declarado por uso de una suscripción nombrada (envuelve HU-06). Busca entre todas las suscripciones del
/// propietario, también las inactivas; si el nombre coincide con varias devuelve candidatos sin ids.
/// </summary>
public sealed class CostPerUseTool(
    IGetCostPerUseHandler handler,
    ISubscriptionRepository repository,
    ICurrentUser currentUser) : FinancialTool
{
    private static readonly string[] Sources = ["cost-per-use", "usage-records"];

    public override string Name => "get_cost_per_use";

    protected override string Description =>
        "Declared cost per use of one subscription: amount paid in the period divided by the declared uses. " +
        "Returns not_calculable when there are no uses in the period.";

    protected override string ParametersJsonSchema => $$$"""
        {"type":"object","properties":{"subscriptionName":{"type":"string","maxLength":{{{Subscription.NameMaxLength}}},"description":"Exact name of the subscription."},"from":{"type":"string","format":"date","description":"Period start, yyyy-MM-dd (optional)."},"to":{"type":"string","format":"date","description":"Period end, yyyy-MM-dd (optional)."}},"required":["subscriptionName"],"additionalProperties":false}
        """;

    protected override async Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        var name = arguments.RequiredString("subscriptionName", Subscription.NameMaxLength);
        var from = arguments.OptionalDate("from");
        var to = arguments.OptionalDate("to");
        if (from > to)
        {
            return ToolResult.Invalid(Name, "from must be on or before to.");
        }

        var all = await repository.ListAllAsync(currentUser.GetOwnerId(), cancellationToken);
        var matches = all.Where(subscription => SameName(name, subscription.Name)).ToList();
        if (matches.Count == 0)
        {
            return NotFound(name);
        }

        if (matches.Count > 1)
        {
            var candidates = matches.Select(subscription => new
            {
                subscription.Name,
                subscription.Currency,
                subscription.Amount,
                BillingCycle = subscription.BillingCycle.ToString(),
                Status = subscription.Status.ToString(),
            }).ToList();
            return new ToolResult(Name, ToolStatus.Ambiguous, new { subscriptionName = name, candidates }, [], ["subscriptions"]);
        }

        var costPerUse = await handler.HandleAsync(matches[0].Id, from, to, cancellationToken);
        if (costPerUse is null)
        {
            return NotFound(name);
        }

        var data = new
        {
            subscriptionName = matches[0].Name,
            costPerUse.Currency,
            costPerUse.From,
            costPerUse.To,
            costPerUse.MonthsInPeriod,
            costPerUse.TotalPaid,
            costPerUse.TotalUses,
            costPerUse.CostPerUse,
            costPerUse.Reason,
        };
        string[] assumptions = [ToolAssumptions.DeclaredUsage, ToolAssumptions.Expected];

        return new ToolResult(Name, costPerUse.Calculable ? ToolStatus.Ok : ToolStatus.NotCalculable, data, assumptions, Sources);
    }

    private ToolResult NotFound(string name) =>
        new(Name, ToolStatus.NotFound, new { subscriptionName = name }, [], ["subscriptions"]);
}
