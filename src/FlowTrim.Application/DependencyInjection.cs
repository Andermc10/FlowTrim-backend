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
        services.AddScoped<IMarkSubscriptionPaidHandler, MarkSubscriptionPaidHandler>();
        services.AddScoped<IListSubscriptionPaymentsHandler, ListSubscriptionPaymentsHandler>();
        services.AddScoped<IGetUpcomingChargesHandler, GetUpcomingChargesHandler>();
        services.AddScoped<Summaries.IGetSummaryHandler, Summaries.GetSummaryHandler>();
        services.AddScoped<Simulations.ISimulateHandler, Simulations.SimulateHandler>();
        services.AddScoped<Usage.IRegisterUsageHandler, Usage.RegisterUsageHandler>();
        services.AddScoped<Usage.IListUsageHandler, Usage.ListUsageHandler>();
        services.AddScoped<Usage.IGetCostPerUseHandler, Usage.GetCostPerUseHandler>();
        services.AddScoped<Analytics.IGetAnalyticsHandler, Analytics.GetAnalyticsHandler>();
        services.AddScoped<Reports.IExportSubscriptionsHandler, Reports.ExportSubscriptionsHandler>();
        services.AddScoped<Imports.IPreviewSubscriptionImportHandler, Imports.PreviewSubscriptionImportHandler>();
        services.AddScoped<Imports.IConfirmSubscriptionImportHandler, Imports.ConfirmSubscriptionImportHandler>();
        services.AddScoped<Accounts.IListAccountsHandler, Accounts.ListAccountsHandler>();
        services.AddScoped<Accounts.IGetAccountHandler, Accounts.GetAccountHandler>();
        services.AddScoped<Accounts.ICreateAccountHandler, Accounts.CreateAccountHandler>();
        services.AddScoped<Accounts.IUpdateAccountHandler, Accounts.UpdateAccountHandler>();
        services.AddScoped<Accounts.IDeleteAccountHandler, Accounts.DeleteAccountHandler>();
        services.AddScoped<Categories.IListCategoriesHandler, Categories.ListCategoriesHandler>();
        services.AddScoped<Categories.ICreateCategoryHandler, Categories.CreateCategoryHandler>();
        services.AddScoped<Categories.IUpdateCategoryHandler, Categories.UpdateCategoryHandler>();
        services.AddScoped<Categories.IDeleteCategoryHandler, Categories.DeleteCategoryHandler>();
        services.AddScoped<Transactions.IListTransactionsHandler, Transactions.ListTransactionsHandler>();
        services.AddScoped<Transactions.IGetTransactionHandler, Transactions.GetTransactionHandler>();
        services.AddScoped<Transactions.ICreateTransactionHandler, Transactions.CreateTransactionHandler>();
        services.AddScoped<Transactions.IUpdateTransactionHandler, Transactions.UpdateTransactionHandler>();
        services.AddScoped<Transactions.IDeleteTransactionHandler, Transactions.DeleteTransactionHandler>();
        services.AddScoped<Dashboard.IGetTodayDashboardHandler, Dashboard.GetTodayDashboardHandler>();

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
