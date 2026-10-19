using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Tests.Api;
using Npgsql;

namespace FlowTrim.Tests.Integration.Api;

/// <summary>
/// API real + infraestructura real + PostgreSQL desechable (HU-47, AC-4..AC-6). Solo la autenticación es
/// falsa (esquema de pruebas de HU-12): no se usa Auth0 ni ninguna credencial real.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RealDatabaseApiTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";

    private readonly PostgresFixture _database;
    private readonly FlowTrimApiFactory _factory;

    public RealDatabaseApiTests(PostgresFixture database)
    {
        _database = database;
        _factory = new FlowTrimApiFactory { DatabaseConnectionString = database.ConnectionString };
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CrudFlow_RealDatabase_PersistsAndReturnsContractStatusCodes()
    {
        var client = _factory.CreateClientFor(OwnerA);

        var created = await client.PostAsJsonAsync("/api/subscriptions", Subscription("Netflix", 38900m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal($"/api/subscriptions/{id}", created.Headers.Location?.OriginalString);

        var fetched = await GetJsonAsync(client, $"/api/subscriptions/{id}");
        Assert.Equal(38900m, fetched.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-01", fetched.GetProperty("nextBillingDate").GetString());

        var updated = await client.PutAsJsonAsync($"/api/subscriptions/{id}",
            Subscription("Netflix Premium", 44900.5m) with { Status = "Inactive" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(["Netflix Premium|44900.50|Inactive|it|owner-a"], await QueryAsync(
            "SELECT name || '|' || amount::text || '|' || status || '|' || owner_id FROM subscriptions"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/subscriptions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/subscriptions/{id}")).StatusCode);
        Assert.Equal(["0"], await QueryAsync("SELECT count(*)::text FROM subscriptions"));
    }

    [Fact]
    public async Task TwoOwners_RealDatabase_AreIsolatedAcrossAllPrivateRoutes()
    {
        var a = _factory.CreateClientFor(OwnerA);
        var b = _factory.CreateClientFor(OwnerB);
        var aId = await CreateAsync(a, "Netflix A", 38900m);
        await CreateAsync(a, "Spotify A", 16900m);
        await CreateAsync(b, "Disney B", 25000m);
        Assert.Equal(HttpStatusCode.Created, (await a.PostAsJsonAsync(
            $"/api/subscriptions/{aId}/usage-records", new { usageDate = "2026-10-10", usageCount = 3 })).StatusCode);

        // Lecturas propias.
        Assert.Equal(2, (await GetJsonAsync(a, "/api/subscriptions")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await GetJsonAsync(b, "/api/subscriptions")).GetProperty("totalCount").GetInt32());
        Assert.Equal(["Netflix A", "Spotify A"], Names(await GetJsonAsync(a, "/api/subscriptions")));
        Assert.Equal(["Disney B"], Names(await GetJsonAsync(b, "/api/subscriptions")));
        Assert.Equal(55800m, MonthlyEquivalent(await GetJsonAsync(a, "/api/summary")));
        Assert.Equal(25000m, MonthlyEquivalent(await GetJsonAsync(b, "/api/summary")));
        Assert.Equal(1, (await GetJsonAsync(b, "/api/subscriptions/upcoming")).GetProperty("items").GetArrayLength());

        // Accesos cruzados de B sobre la suscripción de A: siempre 404 y sin efectos.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/subscriptions/{aId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync(
            $"/api/subscriptions/{aId}", Subscription("Hacked", 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/subscriptions/{aId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync(
            $"/api/subscriptions/{aId}/usage-records", new { usageDate = "2026-10-11", usageCount = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/subscriptions/{aId}/usage-records")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/subscriptions/{aId}/cost-per-use")).StatusCode);

        var simulation = await b.PostAsJsonAsync("/api/simulations", new { subscriptionIds = new[] { aId }, horizons = new[] { 12 } });
        var cop = (await simulation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currencies")[0];
        Assert.Equal(0m, cop.GetProperty("selectedMonthly").GetDecimal());

        Assert.Equal(["Netflix A|1"], await QueryAsync(
            $"SELECT s.name || '|' || count(u.id) FROM subscriptions s LEFT JOIN usage_records u ON u.subscription_id = s.id " +
            $"WHERE s.id = '{aId}' GROUP BY s.name"));
    }

    [Fact]
    public async Task Analytics_RealDatabase_MatchesSummaryMonthlyTotal()
    {
        var a = _factory.CreateClientFor(OwnerA);
        await CreateAsync(a, "Netflix A", 38900m);
        await CreateAsync(a, "Spotify A", 16900m);

        var summary = await GetJsonAsync(a, "/api/summary");
        var analytics = await GetJsonAsync(a, "/api/analytics");

        var cop = analytics.GetProperty("currencies")[0];
        Assert.Equal(MonthlyEquivalent(summary), cop.GetProperty("monthlyTotal").GetDecimal());
        Assert.Equal(2, cop.GetProperty("topSubscriptions").GetArrayLength());
    }

    [Fact]
    public async Task PrivateRoute_WithoutToken_ReturnsUnauthorized_RealDatabase()
    {
        var response = await _factory.CreateClient().GetAsync("/api/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_RealDatabase_IsHealthy()
    {
        var health = await GetJsonAsync(_factory.CreateClient(), "/api/health");

        Assert.Equal("Healthy", health.GetProperty("database").GetString());
    }

    private static SubscriptionPayload Subscription(string name, decimal amount) =>
        new(name, null, amount, "COP", "Monthly", "2026-11-01", "Active");

    private static async Task<Guid> CreateAsync(HttpClient client, string name, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/subscriptions", Subscription(name, amount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).Order().ToArray();

    private static decimal MonthlyEquivalent(JsonElement summary) =>
        summary.GetProperty("currencies")[0].GetProperty("monthlyEquivalent").GetDecimal();

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    // Cuerpo JSON de creación/edición (camelCase por la política por defecto de System.Net.Http.Json).
    private sealed record SubscriptionPayload(
        string Name, string? Category, decimal Amount, string Currency, string BillingCycle,
        string NextBillingDate, string Status);
}
