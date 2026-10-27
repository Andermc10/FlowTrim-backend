using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Insights;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Infrastructure.Health;
using FlowTrim.Infrastructure.Insights;
using FlowTrim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlowTrim.Infrastructure;

/// <summary>
/// Punto de composición de la capa de infraestructura: registra el acceso a datos en el contenedor de DI.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de infraestructura usando la cadena de conexión proporcionada por la capa Api.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // NpgsqlDataSource gestiona el pool de conexiones; lo usa el health check.
        services.AddNpgsqlDataSource(connectionString);

        // DbContext EF Core con mapeo snake_case para toda la base de datos.
        services.AddDbContext<FlowTrimDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddScoped<IDatabaseHealthChecker, NpgsqlDatabaseHealthChecker>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<FlowTrim.Application.Usage.IUsageRecordRepository, UsageRecordRepository>();
        services.AddScoped<FlowTrim.Application.Accounts.IAccountRepository, AccountRepository>();
        services.AddScoped<FlowTrim.Application.Categories.ICategoryRepository, CategoryRepository>();
        services.AddScoped<FlowTrim.Application.Transactions.ITransactionRepository, TransactionRepository>();
        services.AddScoped<FlowTrim.Application.Budgets.IBudgetRepository, BudgetRepository>();
        return services;
    }

    /// <summary>
    /// Registra la frontera con el proveedor de IA (HU-33). La implementación se elige por
    /// <c>Insights:Provider:Kind</c> al resolverla: el simulado por defecto, o el adaptador compatible con OpenAI.
    /// Las opciones (enlace y validación al arrancar) las configura la capa Api.
    /// </summary>
    public static IServiceCollection AddChatModel(this IServiceCollection services)
    {
        services.AddSingleton<FakeChatModel>();
        services.AddHttpClient<OpenAiCompatibleChatModel>(client =>
        {
            // El límite real lo aplica el adaptador con su propio token (Insights:Provider:TimeoutSeconds).
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddTransient<IChatModel>(provider =>
            provider.GetRequiredService<IOptions<ChatModelOptions>>().Value.UsesFake
                ? provider.GetRequiredService<FakeChatModel>()
                : provider.GetRequiredService<OpenAiCompatibleChatModel>());
        return services;
    }
}
