using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>EP-14 HU-75 por HTTP: <c>GET /api/calendar</c>, contrato, mes por defecto, errores y aislamiento.</summary>
public sealed class CalendarApiTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private readonly FlowTrimApiFactory _factory;

    public CalendarApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Calendar_CurrentMonth_ListsExpectedAndRecordedPerDay()
    {
        var owner = $"auth0|calendar-{Guid.NewGuid():N}";
        var client = _factory.CreateClientFor(owner);
        var bank = Account.Create(owner, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var food = Category.Create(owner, "Food", CategoryKind.Expense, "orange", "food", Clock);
        _factory.Accounts.Items.Add(bank);
        _factory.Categories.Items.Add(food);
        await client.PostAsJsonAsync("/api/subscriptions", new
        {
            name = "Netflix", amount = 38900m, currency = "COP", billingCycle = "Monthly", nextBillingDate = "2026-10-28",
            paymentUrl = "https://pay.example.com",
        });
        await client.PostAsJsonAsync("/api/transactions", new
        {
            type = "Expense", accountId = bank.Id, categoryId = food.Id, amount = 25000m, date = "2026-10-20",
        });

        var body = await client.GetFromJsonAsync<JsonElement>("/api/calendar");

        Assert.Equal("2026-10", body.GetProperty("month").GetString());
        Assert.Equal("2026-10-20", body.GetProperty("today").GetString());
        var days = body.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(31, days.Count);
        var charge = Assert.Single(days[27].GetProperty("expected").EnumerateArray());
        Assert.Equal("Netflix", charge.GetProperty("name").GetString());
        Assert.Equal("Subscription", charge.GetProperty("kind").GetString());
        Assert.False(charge.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("https://pay.example.com", charge.GetProperty("paymentUrl").GetString());
        var recorded = Assert.Single(days[19].GetProperty("recorded").EnumerateArray());
        Assert.Equal(25000m, recorded.GetProperty("expense").GetDecimal());
        var totals = Assert.Single(body.GetProperty("totals").EnumerateArray());
        Assert.Equal(38900m, totals.GetProperty("expected").GetDecimal());
        Assert.Equal(25000m, totals.GetProperty("recordedExpense").GetDecimal());
    }

    [Fact]
    public async Task Calendar_OtherMonth_IsEmptyForAnotherOwnersData()
    {
        var owner = _factory.CreateClientFor($"auth0|calendar-{Guid.NewGuid():N}");
        var stranger = _factory.CreateClientFor($"auth0|calendar-{Guid.NewGuid():N}");
        await owner.PostAsJsonAsync("/api/subscriptions", new
        {
            name = "Gym", amount = 90000m, currency = "COP", billingCycle = "Monthly", nextBillingDate = "2026-11-10",
        });

        var mine = await owner.GetFromJsonAsync<JsonElement>("/api/calendar?month=2026-11");
        var theirs = await stranger.GetFromJsonAsync<JsonElement>("/api/calendar?month=2026-11");

        Assert.Equal(30, mine.GetProperty("days").GetArrayLength());
        Assert.Single(mine.GetProperty("totals").EnumerateArray());
        Assert.Empty(theirs.GetProperty("totals").EnumerateArray());
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("october")]
    [InlineData("2026-1")]
    public async Task Calendar_InvalidMonth_Returns400OnMonth(string month)
    {
        var response = await _factory.CreateClientFor("auth0|calendar-invalid").GetAsync($"/api/calendar?month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal("Month must use the yyyy-MM format.", errors.GetProperty("month")[0].GetString());
    }
}
