using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Api.Observability;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowTrim.Tests.Api;

/// <summary>Criterios de HU-41: correlación, línea de log por petición y ausencia de datos privados.</summary>
public sealed class ObservabilityApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Owner = "auth0|observability";
    private readonly FlowTrimApiFactory _factory;

    public ObservabilityApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_ReturnsCorrelationIdentifier()
    {
        var response = await _factory.CreateClientFor(Owner).GetAsync("/api/subscriptions");

        var id = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.Matches("^[0-9a-f]{32}$", id);
    }

    [Fact]
    public async Task Request_WithValidIncomingId_ReusesIt()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "client-req_42.a");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal("client-req_42.a", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task Request_WithUnsafeIncomingId_GeneratesNewOne(string incoming)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, incoming);

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task NotFound_ProblemDetailsCarriesCorrelationId()
    {
        var response = await _factory.CreateClientFor(Owner).GetAsync($"/api/subscriptions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HeaderId(response), body.GetProperty("correlationId").GetString());
        Assert.False(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnhandledException_ProblemDetailsAndLogShareCorrelationId()
    {
        var logs = new LogCapture();
        using var failing = WithServices(logs, services =>
            services.AddSingleton<ISubscriptionRepository, ThrowingSubscriptionRepository>());

        var response = await ClientFor(failing).GetAsync("/api/subscriptions");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var id = HeaderId(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("correlationId").GetString());
        var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(id, error.Scopes[CorrelationIdMiddleware.ScopeKey]);
    }

    [Fact]
    public async Task RequestLog_UsesRouteTemplateStatusAndDuration()
    {
        var logs = new LogCapture();
        using var factory = WithServices(logs, _ => { });
        var id = Guid.NewGuid();

        var response = await ClientFor(factory).GetAsync($"/api/subscriptions/{id}");

        var line = Assert.Single(logs.Entries, e => e.Message.StartsWith("HTTP GET", StringComparison.Ordinal));
        Assert.Matches(@"^HTTP GET /api/subscriptions/\{id:guid\} responded 404 in \d+(\.\d+)? ms$", line.Message);
        Assert.DoesNotContain(id.ToString(), line.Message);
        Assert.Equal(HeaderId(response), line.Scopes[CorrelationIdMiddleware.ScopeKey]);
        Assert.Equal(LogLevel.Information, line.Level);
    }

    [Fact]
    public async Task Request_WithTokenAndQuery_DoesNotLogThem()
    {
        var logs = new LogCapture();
        using var factory = WithServices(logs, _ => { });
        var client = ClientFor(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "secret-token-value-123");

        await client.GetAsync($"/api/subscriptions/{Guid.NewGuid()}/cost-per-use?from=2026-01-01&to=2026-12-31");

        var text = logs.AllText();
        Assert.Contains("HTTP GET /api/subscriptions/{id:guid}/cost-per-use responded", text);
        Assert.DoesNotContain("secret-token-value-123", text);
        Assert.DoesNotContain("2026-01-01", text);
        Assert.DoesNotContain(Owner, text);
    }

    [Fact]
    public async Task Health_DatabaseFailureReturnsSafePayloadWithCorrelationId()
    {
        var logs = new LogCapture();
        using var factory = WithServices(logs, services =>
            services.AddSingleton<IDatabaseHealthChecker, UnhealthyDatabaseChecker>());

        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("api").GetString());
        Assert.Equal("Unhealthy", body.GetProperty("database").GetString());
        Assert.NotEmpty(HeaderId(response));
        // El health check se registra en Debug para no inundar los logs.
        Assert.All(logs.Entries.Where(e => e.Message.Contains("/api/health")), e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public async Task Cors_ExposesCorrelationHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", FlowTrimApiFactory.TestFrontendOrigin);

        var response = await _factory.CreateClient().SendAsync(request);

        var exposed = string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains(CorrelationIdMiddleware.HeaderName, exposed, StringComparison.OrdinalIgnoreCase);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithServices(
        LogCapture logs, Action<IServiceCollection> configure) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
            builder.ConfigureTestServices(configure);
        });

    private static HttpClient ClientFor(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, Owner);
        return client;
    }

    private static string HeaderId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

    private sealed class UnhealthyDatabaseChecker : IDatabaseHealthChecker
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class ThrowingSubscriptionRepository : ISubscriptionRepository
    {
        private static Exception Failure() => new InvalidOperationException("Simulated persistence failure.");

        public Task AddAsync(Subscription subscription, CancellationToken cancellationToken) => throw Failure();
        public Task<Subscription?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) => throw Failure();
        public Task<IReadOnlyList<Subscription>> ListAsync(string ownerId, int skip, int take, CancellationToken cancellationToken) => throw Failure();
        public Task<int> CountAsync(string ownerId, CancellationToken cancellationToken) => throw Failure();
        public Task<IReadOnlyList<Subscription>> ListActiveAsync(string ownerId, CancellationToken cancellationToken) => throw Failure();
        public Task<IReadOnlyList<Subscription>> ListAllAsync(string ownerId, CancellationToken cancellationToken) => throw Failure();
        public Task AddImportedAsync(string ownerId, Func<IReadOnlyList<Subscription>, IReadOnlyList<Subscription>> selectNew,
            CancellationToken cancellationToken) => throw Failure();
        public Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken) => throw Failure();
        public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) => throw Failure();
    }
}
