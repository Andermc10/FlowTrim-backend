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
        return services;
    }
}
