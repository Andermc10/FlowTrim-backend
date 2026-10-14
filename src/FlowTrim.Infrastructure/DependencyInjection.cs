using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Infrastructure.Health;
using FlowTrim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
