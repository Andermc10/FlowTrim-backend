using FlowTrim.Application.Analytics;
using FlowTrim.Application.Insights.Tools;
using FlowTrim.Application.Simulations;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Summaries;
using FlowTrim.Application.Usage;

namespace FlowTrim.Tests.Common;

/// <summary>Registro de herramientas de HU-34 con los handlers reales sobre repositorios de prueba.</summary>
public static class TestFinancialTools
{
    public static FinancialToolRegistry Create(
        ISubscriptionRepository subscriptions,
        IUsageRecordRepository usage,
        TimeProvider clock,
        string owner)
    {
        var user = new TestCurrentUser(owner);
        return new FinancialToolRegistry(
        [
            new SpendingSummaryTool(new GetSummaryHandler(subscriptions, user)),
            new SpendingAnalyticsTool(new GetAnalyticsHandler(subscriptions, clock, user)),
            new UpcomingChargesTool(new GetUpcomingChargesHandler(subscriptions, clock, user)),
            new SimulateCancellationTool(new SimulateHandler(subscriptions, user), subscriptions, user),
            new CostPerUseTool(new GetCostPerUseHandler(subscriptions, usage, user), subscriptions, user),
        ]);
    }
}
