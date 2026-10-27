using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Transactions;
using FlowTrim.Application.Usage;
using FlowTrim.Tests.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Api;

/// <summary>
/// Levanta la API real en memoria con autenticación falsa y repositorios en memoria. No necesita base de
/// datos ni tokens reales. Con <see cref="DatabaseConnectionString"/> usa en cambio la infraestructura real
/// contra el PostgreSQL desechable de las pruebas de integración (HU-47); la autenticación siempre es falsa.
/// </summary>
public sealed class FlowTrimApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Authority configurada; una prueba la deja vacía para verificar el fallo de arranque.</summary>
    public string Authority { get; init; } = "https://issuer.test/";

    /// <summary>Origen permitido del frontend (reglas de producción: https exacto). Vacío = sin orígenes.</summary>
    public string AllowedOrigin { get; init; } = TestFrontendOrigin;

    public const string TestFrontendOrigin = "https://app.flowtrim.test";

    /// <summary>
    /// Cadena de conexión de un PostgreSQL real. Nula = repositorios y health check en memoria.
    /// </summary>
    public string? DatabaseConnectionString { get; init; }

    /// <summary>Registra los pagos (HU-73) en la misma lista de movimientos que usan las cuentas.</summary>
    public InMemorySubscriptionRepository Subscriptions => _subscriptions ??= new() { Payments = Accounts.Transactions };

    private InMemorySubscriptionRepository? _subscriptions;

    public InMemoryUsageRecordRepository UsageRecords { get; } = new();

    public InMemoryAccountRepository Accounts { get; } = new();

    public InMemoryBudgetRepository Budgets { get; } = new();

    /// <summary>Comparte la lista de movimientos con <see cref="Accounts"/>.</summary>
    public InMemoryCategoryRepository Categories => _categories ??= new() { Transactions = Accounts.Transactions };

    private InMemoryCategoryRepository? _categories;

    /// <summary>Movimientos sobre la misma lista que usan las cuentas para sus saldos.</summary>
    public InMemoryTransactionRepository Transactions => _transactions ??= new(Accounts);

    private InMemoryTransactionRepository? _transactions;

    /// <summary>"Ahora" de la API en las pruebas: 2026-10-20 10:00 en Bogotá (hoy = 2026-10-20 en el calendario del producto).</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Reloj fijo para que las reglas de fecha (HU-56) no dependan del día en que corren las pruebas.</summary>
    public TimeProvider Clock { get; init; } = new FixedTimeProvider(DefaultNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Entorno propio: no carga User Secrets ni appsettings.Development.json del desarrollador.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FlowTrimDb", DatabaseConnectionString ?? "Host=localhost;Database=unused");
        builder.UseSetting("Authentication:Authority", Authority);
        builder.UseSetting("Authentication:Audience", "flowtrim-api-tests");
        if (!string.IsNullOrEmpty(AllowedOrigin))
        {
            builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            services.AddSingleton(Clock);

            if (DatabaseConnectionString is not null)
            {
                return;
            }

            services.AddSingleton<ISubscriptionRepository>(Subscriptions);
            services.AddSingleton<IUsageRecordRepository>(UsageRecords);
            services.AddSingleton<IAccountRepository>(Accounts);
            services.AddSingleton<ICategoryRepository>(Categories);
            services.AddSingleton<ITransactionRepository>(Transactions);
            services.AddSingleton<FlowTrim.Application.Budgets.IBudgetRepository>(Budgets);
            services.AddSingleton<IDatabaseHealthChecker, HealthyDatabaseChecker>();
        });
    }

    private sealed class HealthyDatabaseChecker : IDatabaseHealthChecker
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}

/// <summary>Clientes autenticados para la factoría base y para las derivadas con <c>WithWebHostBuilder</c>.</summary>
public static class ApiClientExtensions
{
    /// <summary>Cliente HTTP autenticado como el subject indicado.</summary>
    public static HttpClient CreateClientFor(this WebApplicationFactory<Program> factory, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, subject);
        return client;
    }
}
