using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowTrim.Tests.Api;

/// <summary>EP-14 HU-74 por HTTP: crear un plan de cuotas, pagarlo hasta completarlo y validar los conteos.</summary>
public sealed class InstalmentPlansApiTests : IClassFixture<FlowTrimApiFactory>
{
    private readonly FlowTrimApiFactory _factory;

    public InstalmentPlansApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Plan_PaidUntilTheLastInstalment_IsCompletedAndInactive()
    {
        var client = NewClient();
        var created = await client.PostAsJsonAsync("/api/subscriptions", Payload(total: 3, paid: 1));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("Instalment", body.GetProperty("kind").GetString());
        Assert.Equal(3, body.GetProperty("totalInstalments").GetInt32());
        Assert.Equal(1, body.GetProperty("paidInstalments").GetInt32());

        var second = await (await client.PostAsync($"/api/subscriptions/{id}/mark-paid", null)).Content.ReadFromJsonAsync<JsonElement>();
        var third = await (await client.PostAsync($"/api/subscriptions/{id}/mark-paid", null)).Content.ReadFromJsonAsync<JsonElement>();
        var fourth = await client.PostAsync($"/api/subscriptions/{id}/mark-paid", null);

        Assert.False(second.GetProperty("completed").GetBoolean());
        Assert.Equal(2, second.GetProperty("subscription").GetProperty("paidInstalments").GetInt32());
        Assert.True(third.GetProperty("completed").GetBoolean());
        Assert.Equal("Inactive", third.GetProperty("subscription").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
    }

    [Theory]
    [InlineData(null, null, "totalInstalments")]
    [InlineData(601, null, "totalInstalments")]
    [InlineData(4, 4, "paidInstalments")]
    public async Task Create_InvalidCounts_Returns400OnTheField(int? total, int? paid, string field)
    {
        var response = await NewClient().PostAsJsonAsync("/api/subscriptions", Payload(total, paid));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Update_KeepsPaidInstalmentsAndRejectsATotalBelowThem()
    {
        var client = NewClient();
        var id = (await (await client.PostAsJsonAsync("/api/subscriptions", Payload(total: 12, paid: 5)))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var raised = await client.PutAsJsonAsync($"/api/subscriptions/{id}", UpdatePayload(total: 18));
        var lowered = await client.PutAsJsonAsync($"/api/subscriptions/{id}", UpdatePayload(total: 4));

        var body = await raised.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(18, body.GetProperty("totalInstalments").GetInt32());
        Assert.Equal(5, body.GetProperty("paidInstalments").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, lowered.StatusCode);
    }

    private HttpClient NewClient() => _factory.CreateClientFor($"auth0|instalments-{Guid.NewGuid():N}");

    private static object Payload(int? total, int? paid) => new
    {
        name = "Laptop", amount = 250000m, currency = "COP", billingCycle = "Monthly", nextBillingDate = "2026-10-25",
        kind = "Instalment", totalInstalments = total, paidInstalments = paid,
    };

    private static object UpdatePayload(int total) => new
    {
        name = "Laptop", amount = 250000m, currency = "COP", billingCycle = "Monthly", nextBillingDate = "2026-10-25",
        status = "Active", kind = "Instalment", totalInstalments = total,
    };
}
