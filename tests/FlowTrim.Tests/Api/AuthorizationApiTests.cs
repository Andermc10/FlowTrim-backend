using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>Criterios de aceptación de HU-12 verificados a través de HTTP.</summary>
public sealed class AuthorizationApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|owner-a";
    private const string OwnerB = "auth0|owner-b";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);

    private readonly FlowTrimApiFactory _factory;

    public AuthorizationApiTests(FlowTrimApiFactory factory)
    {
        // xUnit crea una instancia por prueba: se limpia el estado compartido de la factory.
        _factory = factory;
        _factory.Subscriptions.Items.Clear();
        _factory.UsageRecords.Items.Clear();
    }

    public static TheoryData<string, string> PrivateRoutes()
    {
        var id = Guid.NewGuid();
        return new TheoryData<string, string>
        {
            { "GET", "/api/subscriptions" },
            { "POST", "/api/subscriptions" },
            { "GET", "/api/subscriptions/upcoming" },
            { "GET", $"/api/subscriptions/{id}" },
            { "PUT", $"/api/subscriptions/{id}" },
            { "DELETE", $"/api/subscriptions/{id}" },
            { "POST", $"/api/subscriptions/{id}/mark-paid" },
            { "GET", "/api/summary" },
            { "POST", $"/api/subscriptions/{id}/usage-records" },
            { "GET", $"/api/subscriptions/{id}/usage-records" },
            { "GET", $"/api/subscriptions/{id}/cost-per-use" },
            { "POST", "/api/simulations" },
            { "GET", "/api/analytics" },
            { "GET", "/api/reports/subscriptions.csv" },
            { "GET", "/api/accounts" },
            { "POST", "/api/accounts" },
            { "GET", $"/api/accounts/{id}" },
            { "PUT", $"/api/accounts/{id}" },
            { "DELETE", $"/api/accounts/{id}" },
            { "GET", "/api/categories" },
            { "POST", "/api/categories" },
            { "PUT", $"/api/categories/{id}" },
            { "DELETE", $"/api/categories/{id}" },
        };
    }

    [Theory]
    [MemberData(nameof(PrivateRoutes))]
    public async Task PrivateRoute_WithoutToken_ReturnsUnauthorized(string method, string url)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { }),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithoutToken_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutSubject_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.NoSubjectHeader, "true");

        var response = await client.GetAsync("/api/subscriptions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AssignsOwnerFromToken()
    {
        var client = _factory.CreateClientFor(OwnerA);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            name = "Netflix",
            amount = 38900m,
            currency = "COP",
            billingCycle = "Monthly",
            nextBillingDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(_factory.Subscriptions.Items);
        Assert.Equal(OwnerA, stored.OwnerId);
    }

    [Fact]
    public async Task SubscriptionList_TwoOwners_ReturnsOnlyCurrentOwnerRows()
    {
        Seed("Owner A service", OwnerA);
        Seed("Owner B service", OwnerB);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/subscriptions");

        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Owner A service", item.GetProperty("name").GetString());
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task NonOwnerSubscription_GetUpdateDelete_ReturnsNotFound()
    {
        var ownerB = Seed("Owner B service", OwnerB);
        var client = _factory.CreateClientFor(OwnerA);
        var url = $"/api/subscriptions/{ownerB.Id}";

        var get = await client.GetAsync(url);
        var put = await client.PutAsJsonAsync(url, new
        {
            name = "Changed",
            amount = 10m,
            currency = "COP",
            billingCycle = "Monthly",
            nextBillingDate = "2026-11-01",
            status = "Active",
        });
        var delete = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Contains(ownerB, _factory.Subscriptions.Items);
        Assert.Equal("Owner B service", ownerB.Name);
    }

    [Fact]
    public async Task Summary_UsesCurrentOwnerOnly()
    {
        Seed("Owner A service", OwnerA, 100m);
        Seed("Owner B service", OwnerB, 900m);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/summary");

        var currency = Assert.Single(body.GetProperty("currencies").EnumerateArray());
        Assert.Equal(100m, currency.GetProperty("monthlyEquivalent").GetDecimal());
    }

    [Fact]
    public async Task UpcomingCharges_UsesCurrentOwnerOnly()
    {
        Seed("Owner A service", OwnerA);
        Seed("Owner B service", OwnerB);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/subscriptions/upcoming");

        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Owner A service", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Usage_OtherOwnerSubscription_ReturnsNotFound()
    {
        var ownerB = Seed("Owner B service", OwnerB);
        var client = _factory.CreateClientFor(OwnerA);

        var register = await client.PostAsJsonAsync(
            $"/api/subscriptions/{ownerB.Id}/usage-records",
            new { usageDate = "2026-10-10", usageCount = 3 });
        var list = await client.GetAsync($"/api/subscriptions/{ownerB.Id}/usage-records");
        var costPerUse = await client.GetAsync($"/api/subscriptions/{ownerB.Id}/cost-per-use");

        Assert.Equal(HttpStatusCode.NotFound, register.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, costPerUse.StatusCode);
        Assert.Empty(_factory.UsageRecords.Items);
    }

    [Fact]
    public async Task Simulation_WithOtherOwnerSelection_IgnoresIt()
    {
        var ownerB = Seed("Owner B service", OwnerB, 900m);
        var client = _factory.CreateClientFor(OwnerA);

        var response = await client.PostAsJsonAsync(
            "/api/simulations",
            new { subscriptionIds = new[] { ownerB.Id }, horizons = new[] { 12 } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("currencies").EnumerateArray());
    }

    [Fact]
    public void Startup_WithoutAuthority_Fails()
    {
        using var factory = new FlowTrimApiFactory { Authority = "" };

        // Solo se comprueba que el host no arranca: según la carrera entre el fallo y la liberación del
        // host, WebApplicationFactory puede entregar la excepción original u otra (ObjectDisposedException).
        // El contenido del mensaje se prueba de forma determinista en ConfigurationValidationTests.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private Subscription Seed(string name, string ownerId, decimal amount = 10m)
    {
        var subscription = Subscription.Create(
            ownerId,
            name,
            null,
            amount,
            "COP",
            BillingCycle.Monthly,
            new DateOnly(2026, 11, 1),
            new FixedTimeProvider(FixedNow));
        _factory.Subscriptions.Items.Add(subscription);
        return subscription;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
