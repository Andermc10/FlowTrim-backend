using FlowTrim.Application.Insights.Tools;
using FlowTrim.Application.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Application;

/// <summary>Punto de composición de la capa de aplicación: casos de uso y servicios transversales.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Reloj inyectable para que la lógica de fechas sea testeable (nunca DateTime.UtcNow directo).
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICreateSubscriptionHandler, CreateSubscriptionHandler>();
        services.AddScoped<IListSubscriptionsHandler, ListSubscriptionsHandler>();
        services.AddScoped<IGetSubscriptionHandler, GetSubscriptionHandler>();
        services.AddScoped<IUpdateSubscriptionHandler, UpdateSubscriptionHandler>();
        services.AddScoped<IDeleteSubscriptionHandler, DeleteSubscriptionHandler>();
        services.AddScoped<IGetUpcomingChargesHandler, GetUpcomingChargesHandler>();
        services.AddScoped<Summaries.IGetSummaryHandler, Summaries.GetSummaryHandler>();
        services.AddScoped<Simulations.ISimulateHandler, Simulations.SimulateHandler>();
        services.AddScoped<Usage.IRegisterUsageHandler, Usage.RegisterUsageHandler>();
        services.AddScoped<Usage.IListUsageHandler, Usage.ListUsageHandler>();
        services.AddScoped<Usage.IGetCostPerUseHandler, Usage.GetCostPerUseHandler>();
        services.AddScoped<Analytics.IGetAnalyticsHandler, Analytics.GetAnalyticsHandler>();
        services.AddScoped<Reports.IExportSubscriptionsHandler, Reports.ExportSubscriptionsHandler>();

        // Herramientas financieras de solo lectura para Insights (HU-34): esta lista es la allow-list completa.
        services.AddScoped<IFinancialTool, SpendingSummaryTool>();
        services.AddScoped<IFinancialTool, SpendingAnalyticsTool>();
        services.AddScoped<IFinancialTool, UpcomingChargesTool>();
        services.AddScoped<IFinancialTool, SimulateCancellationTool>();
        services.AddScoped<IFinancialTool, CostPerUseTool>();
        services.AddScoped<FinancialToolRegistry>();
        services.AddScoped<Insights.IAnswerQuestionHandler, Insights.AnswerQuestionHandler>();
        return services;
    }
}
