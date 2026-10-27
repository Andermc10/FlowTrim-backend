using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowTrim.Tests.Api;

/// <summary>EP-15 HU-77 por HTTP: metas, aportes y retiros, cifras, validación, conflictos y aislamiento. "Hoy" = 2026-10-20.</summary>
public sealed class GoalsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private readonly FlowTrimApiFactory _factory;

    public GoalsApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Goal_WithContributions_ShowsRequiredPerMonthAndTheEstimate()
    {
        // AC-1 + AC-2 por HTTP.
        var client = NewClient();
        var id = await CreateAsync(client, new { name = "Trip", currency = "COP", target = 3000000m, targetDate = "2027-03-31" });
        foreach (var date in new[] { "2026-07-10", "2026-08-10", "2026-09-10" })
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 300000m, date })).StatusCode);
        }

        await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = -300000m, date = "2026-10-05", note = "Car repair" });
        await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 300000m, date = "2026-10-15" });

        var goal = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/goals")).EnumerateArray());
        Assert.Equal(900000m, goal.GetProperty("saved").GetDecimal());
        Assert.Equal(2100000m, goal.GetProperty("remaining").GetDecimal());
        Assert.Equal(6, goal.GetProperty("monthsLeft").GetInt32());
        Assert.Equal(350000m, goal.GetProperty("requiredPerMonth").GetDecimal());
        Assert.Equal(300000m, goal.GetProperty("averagePerMonth").GetDecimal());
        Assert.Equal("2027-05-01", goal.GetProperty("estimatedCompletion").GetString());
        Assert.Equal(5, goal.GetProperty("contributionsCount").GetInt32());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/goals/{id}");
        var contributions = detail.GetProperty("contributions").EnumerateArray().ToList();
        Assert.Equal(5, contributions.Count);
        Assert.Equal("2026-10-15", contributions[0].GetProperty("date").GetString());
        Assert.Equal("Car repair", contributions[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task Contributions_InvalidOrTooLargeWithdrawals_Return400()
    {
        // AC-3: un retiro no deja lo ahorrado en negativo.
        var client = NewClient();
        var id = await CreateAsync(client, new { name = "Laptop", currency = "COP", target = 1000m });
        await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 200m, date = "2026-10-20" });

        var withdrawal = await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = -201m, date = "2026-10-20" });
        var future = await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 10m, date = "2026-10-21" });
        var zero = await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 0m, date = "2026-10-20" });

        Assert.Equal("A withdrawal cannot be larger than what is saved.", (await ErrorsAsync(withdrawal)).GetProperty("amount")[0].GetString());
        Assert.Equal("The date cannot be in the future.", (await ErrorsAsync(future)).GetProperty("date")[0].GetString());
        Assert.True((await ErrorsAsync(zero)).TryGetProperty("amount", out _));
    }

    [Fact]
    public async Task Goal_ReachingTheTarget_IsCompletedAndListedLast()
    {
        var client = NewClient();
        var done = await CreateAsync(client, new { name = "Phone", currency = "COP", target = 500m });
        await CreateAsync(client, new { name = "House", currency = "COP", target = 9000m, targetDate = "2030-01-01" });
        await client.PostAsJsonAsync($"/api/goals/{done}/contributions", new { amount = 500m, date = "2026-10-20" });

        var goals = (await client.GetFromJsonAsync<JsonElement>("/api/goals")).EnumerateArray().ToList();

        Assert.Equal(["House", "Phone"], goals.Select(g => g.GetProperty("name").GetString()));
        Assert.True(goals[1].GetProperty("completed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, goals[1].GetProperty("estimatedCompletion").ValueKind);
    }

    [Fact]
    public async Task Update_CurrencyLockedOnceThereAreContributions_AndPastDatesOnlyIfUnchanged()
    {
        var client = NewClient();
        var id = await CreateAsync(client, new { name = "Trip", currency = "COP", target = 1000m, targetDate = "2026-12-31" });
        var pastOnCreate = await client.PostAsJsonAsync("/api/goals", new { name = "Old", currency = "COP", target = 10m, targetDate = "2026-10-19" });
        await client.PostAsJsonAsync($"/api/goals/{id}/contributions", new { amount = 100m, date = "2026-10-20" });

        var currency = await client.PutAsJsonAsync($"/api/goals/{id}", new { name = "Trip", currency = "USD", target = 1000m, targetDate = "2026-12-31" });
        var renamed = await client.PutAsJsonAsync($"/api/goals/{id}", new { name = "Big trip", currency = "cop", target = 2000m, targetDate = "2026-12-31" });

        Assert.Equal("The target date cannot be in the past.", (await ErrorsAsync(pastOnCreate)).GetProperty("targetDate")[0].GetString());
        Assert.Equal(HttpStatusCode.Conflict, currency.StatusCode);
        Assert.Equal("goal_currency_locked", (await currency.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("Big trip", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeleteContribution_ThatWouldLeaveTheGoalNegative_Returns409_AndOtherOwnersGet404()
    {
        var client = NewClient();
        var stranger = NewClient();
        var id = await CreateAsync(client, new { name = "Trip", currency = "COP", target = 1000m });
        var deposit = await ContributeAsync(client, id, 100m);
        var withdrawal = await ContributeAsync(client, id, -60m);

        var blocked = await client.DeleteAsync($"/api/goals/{id}/contributions/{deposit}");
        var foreign = await stranger.DeleteAsync($"/api/goals/{id}/contributions/{withdrawal}");
        var foreignGoal = await stranger.GetAsync($"/api/goals/{id}");
        var removedWithdrawal = await client.DeleteAsync($"/api/goals/{id}/contributions/{withdrawal}");
        var removedGoal = await client.DeleteAsync($"/api/goals/{id}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("goal_saved_negative", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignGoal.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedWithdrawal.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedGoal.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/goals")).EnumerateArray());
    }

    private HttpClient NewClient() => _factory.CreateClientFor($"auth0|goals-{Guid.NewGuid():N}");

    private static async Task<Guid> CreateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/goals", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ContributeAsync(HttpClient client, Guid goalId, decimal amount) =>
        (await (await client.PostAsJsonAsync($"/api/goals/{goalId}/contributions", new { amount, date = "2026-10-20" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
}
