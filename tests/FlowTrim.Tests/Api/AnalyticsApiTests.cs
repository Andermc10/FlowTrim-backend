using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>HU-50 a través de HTTP: aislamiento por dueño, validación y forma de la respuesta.</summary>
public sealed class AnalyticsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|analytics-a";
    private const string OwnerB = "auth0|analytics-b";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly FlowTrimApiFactory _factory;

    public AnalyticsApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Subscriptions.Items.Clear();
    }

    [Fact]
    public async Task Analytics_TwoOwners_AreIsolated()
    {
        Seed(OwnerA, "Netflix", 38900m, "Streaming");
        Seed(OwnerB, "Gym", 90000m, "Health");

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/analytics");

        var cop = Assert.Single(body.GetProperty("currencies").EnumerateArray());
        Assert.Equal(38900m, cop.GetProperty("monthlyTotal").GetDecimal());
        Assert.Equal("Streaming", Assert.Single(cop.GetProperty("byCategory").EnumerateArray()).GetProperty("category").GetString());
        Assert.Equal("Netflix", Assert.Single(cop.GetProperty("topSubscriptions").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(12, cop.GetProperty("expectedCharges").GetArrayLength());
        Assert.Matches(@"^\d{4}-\d{2}$", body.GetProperty("referenceMonth").GetString()!);
        Assert.Matches(@"^\d{4}-\d{2}$", cop.GetProperty("expectedCharges")[0].GetProperty("month").GetString()!);
    }

    [Fact]
    public async Task Analytics_MonthsParameter_ControlsTheWindow()
    {
        Seed(OwnerA, "Netflix", 38900m, null);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/analytics?months=24");

        Assert.Equal(24, body.GetProperty("currencies")[0].GetProperty("expectedCharges").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public async Task Analytics_InvalidMonths_ReturnsBadRequest(int months)
    {
        var response = await _factory.CreateClientFor(OwnerA).GetAsync($"/api/analytics?months={months}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("months", out _));
    }

    [Fact]
    public async Task Analytics_WithoutSubscriptions_ReturnsEmptyCurrencies()
    {
        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA), "/api/analytics");

        Assert.Equal(0, body.GetProperty("currencies").GetArrayLength());
    }

    [Fact]
    public async Task Analytics_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/analytics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private void Seed(string ownerId, string name, decimal amount, string? category) =>
        _factory.Subscriptions.Items.Add(Subscription.Create(
            ownerId, name, category, amount, "COP", BillingCycle.Monthly, new DateOnly(2026, 11, 1), new FixedTimeProvider(FixedNow)));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
