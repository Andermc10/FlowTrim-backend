using FlowTrim.Infrastructure;
using FlowTrim.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FlowTrim.Tests.Integration;

/// <summary>
/// PostgreSQL desechable para toda la ejecución de pruebas (Testcontainers). Se crea vacío, recibe las
/// migraciones reales y se destruye al terminar; nunca toca la base de datos local del desarrollador.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Misma versión mayor que docker-compose.yml; la contraseña la genera Testcontainers.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>DbContext configurado igual que en producción (Npgsql + snake_case).</summary>
    public FlowTrimDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FlowTrimDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>
    /// Contenedor de DI con la infraestructura real registrada por <c>AddInfrastructure</c>; los
    /// repositorios (internos) se obtienen por sus interfaces, igual que en la API.
    /// </summary>
    public ServiceProvider CreateInfrastructureServices()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(ConnectionString);
        return services.BuildServiceProvider();
    }

    /// <summary>Deja las tablas vacías para que cada prueba empiece desde cero.</summary>
    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE usage_records, subscriptions;");
    }
}

/// <summary>
/// Agrupa las pruebas con base de datos: comparten el contenedor y se ejecutan en serie.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
