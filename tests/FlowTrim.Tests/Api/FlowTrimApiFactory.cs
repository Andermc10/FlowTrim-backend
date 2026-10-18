using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Subscriptions;
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
/// datos ni tokens reales; la validación contra PostgreSQL real pertenece a HU-47.
/// </summary>
public sealed class FlowTrimApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Authority configurada; una prueba la deja vacía para verificar el fallo de arranque.</summary>
    public string Authority { get; init; } = "https://issuer.test/";

    /// <summary>Origen permitido del frontend (reglas de producción: https exacto). Vacío = sin orígenes.</summary>
    public string AllowedOrigin { get; init; } = TestFrontendOrigin;

    public const string TestFrontendOrigin = "https://app.flowtrim.test";

    public InMemorySubscriptionRepository Subscriptions { get; } = new();

    public InMemoryUsageRecordRepository UsageRecords { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Entorno propio: no carga User Secrets ni appsettings.Development.json del desarrollador.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FlowTrimDb", "Host=localhost;Database=unused");
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

            services.AddSingleton<ISubscriptionRepository>(Subscriptions);
            services.AddSingleton<IUsageRecordRepository>(UsageRecords);
            services.AddSingleton<IDatabaseHealthChecker, HealthyDatabaseChecker>();
        });
    }

    /// <summary>Cliente HTTP autenticado como el subject indicado.</summary>
    public HttpClient CreateClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, subject);
        return client;
    }

    private sealed class HealthyDatabaseChecker : IDatabaseHealthChecker
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
