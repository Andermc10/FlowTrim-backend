using FlowTrim.Application.Abstractions;
using FlowTrim.Infrastructure.Health;
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
        // NpgsqlDataSource gestiona el pool de conexiones; se registra como singleton.
        services.AddNpgsqlDataSource(connectionString);
        services.AddScoped<IDatabaseHealthChecker, NpgsqlDatabaseHealthChecker>();
        return services;
    }
}
