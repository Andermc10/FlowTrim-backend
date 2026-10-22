using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowTrim.Tests.Api;

/// <summary>HU-56 por HTTP: con el reloj fijo de la factoría, "hoy" es 2026-10-20 en Bogotá.</summary>
public sealed class DateRulesApiTests : IClassFixture<FlowTrimApiFactory>
{
    private readonly FlowTrimApiFactory _factory;

    public DateRulesApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_NextBillingDateYesterday_Returns400WithFieldError()
    {
        var client = _factory.CreateClientFor($"auth0|dates-{Guid.NewGuid():N}");

        var yesterday = await client.PostAsJsonAsync("/api/subscriptions", Payload("2026-10-19"));
        var today = await client.PostAsJsonAsync("/api/subscriptions", Payload("2026-10-20"));

        Assert.Equal(HttpStatusCode.BadRequest, yesterday.StatusCode);
        var errors = (await yesterday.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal("Next billing date cannot be in the past.", errors.GetProperty("nextBillingDate")[0].GetString());
        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
    }

    [Fact]
    public async Task RegisterUsage_Tomorrow_Returns400WithFieldError()
    {
        var client = _factory.CreateClientFor($"auth0|dates-{Guid.NewGuid():N}");
        var created = await client.PostAsJsonAsync("/api/subscriptions", Payload("2026-11-01"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/subscriptions/{id}/usage-records", new { usageDate = "2026-10-21", usageCount = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal("Usage date cannot be in the future.", errors.GetProperty("usageDate")[0].GetString());
    }

    private static object Payload(string nextBillingDate) => new
    {
        name = "Netflix",
        amount = 38900m,
        currency = "COP",
        billingCycle = "Monthly",
        nextBillingDate,
    };
}
