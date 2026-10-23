using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Application.Dashboard;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Api;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Dashboard;

/// <summary>Caso de uso y contrato HTTP del panel "Hoy" (HU-68) con los repositorios en memoria.</summary>
public sealed class TodayDashboardTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|owner-a";
    private const string OwnerB = "auth0|owner-b";

    // DefaultNow = 2026-10-20 10:00 en Bogotá: el AC-1 de la spec usa justo ese día de un mes de 31 días.
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);

    private readonly FlowTrimApiFactory _factory;

    public TodayDashboardTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Accounts.Items.Clear();
        _factory.Accounts.Transactions.Clear();
        _factory.Categories.Items.Clear();
        _factory.Subscriptions.Items.Clear();
    }

    [Fact]
    public async Task Today_SpecExample_ReturnsTheFormulaInputsAndSafeToSpend()
    {
        SeedSpecExample(OwnerA);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA));

        Assert.Equal("2026-10-20", body.GetProperty("referenceDate").GetString());
        var cop = Assert.Single(body.GetProperty("currencies").EnumerateArray());
        Assert.Equal(1200000m, cop.GetProperty("available").GetDecimal());
        Assert.Equal(38900m, cop.GetProperty("committedUntilMonthEnd").GetDecimal());
        Assert.Equal(12, cop.GetProperty("daysLeft").GetInt32());
        Assert.Equal(96758.33m, cop.GetProperty("safeToSpendToday").GetDecimal());
        Assert.Equal(3, body.GetProperty("accounts").GetArrayLength());
        var charge = Assert.Single(body.GetProperty("chargesUntilMonthEnd").EnumerateArray());
        Assert.Equal("Netflix", charge.GetProperty("name").GetString());
        Assert.False(charge.GetProperty("isOverdue").GetBoolean());
    }

    [Fact]
    public async Task Today_SumsMonthAndTodaySeparately_AndKeepsTheLastFiveTransactions()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 100000m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(OwnerA, "Salary", CategoryKind.Income, "green", "salary", Clock);
        _factory.Accounts.Items.Add(bank);
        _factory.Categories.Items.AddRange([food, salary]);
        Record(OwnerA, TransactionType.Expense, bank, food, 1000m, new DateOnly(2026, 9, 30));
        Record(OwnerA, TransactionType.Income, bank, salary, 5000m, new DateOnly(2026, 10, 1));
        foreach (var day in new[] { 5, 10, 15, 20, 20 })
        {
            Record(OwnerA, TransactionType.Expense, bank, food, 100m, new DateOnly(2026, 10, day));
        }

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA));

        var cop = Assert.Single(body.GetProperty("currencies").EnumerateArray());
        Assert.Equal(5000m, cop.GetProperty("monthIncome").GetDecimal());
        Assert.Equal(500m, cop.GetProperty("monthExpenses").GetDecimal());
        Assert.Equal(4500m, cop.GetProperty("monthNet").GetDecimal());
        Assert.Equal(200m, cop.GetProperty("todayExpenses").GetDecimal());
        // Saldo 100.000 − 1.000 + 5.000 − 500 = 103.500; 12 días.
        Assert.Equal(103500m, cop.GetProperty("available").GetDecimal());
        var recent = body.GetProperty("recentTransactions").EnumerateArray().ToList();
        Assert.Equal(GetTodayDashboardHandler.RecentTransactionsCount, recent.Count);
        Assert.Equal("2026-10-20", recent[0].GetProperty("date").GetString());
        Assert.Equal("Food", recent[0].GetProperty("categoryName").GetString());
    }

    [Fact]
    public async Task Today_AfterSevenPmInBogota_StillUsesTheSameDay()
    {
        // 2026-11-01 03:00 UTC = 2026-10-31 22:00 en Bogotá: último día de octubre, un día restante.
        using var lateFactory = new FlowTrimApiFactory { Clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero)) };
        lateFactory.Accounts.Items.Add(Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 1000m, Clock));

        var body = await GetJsonAsync(lateFactory.CreateClientFor(OwnerA));

        Assert.Equal("2026-10-31", body.GetProperty("referenceDate").GetString());
        Assert.Equal(1, Assert.Single(body.GetProperty("currencies").EnumerateArray()).GetProperty("daysLeft").GetInt32());
    }

    [Fact]
    public async Task Today_NewUser_ReturnsEmptyLists()
    {
        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA));

        Assert.Empty(body.GetProperty("currencies").EnumerateArray());
        Assert.Empty(body.GetProperty("accounts").EnumerateArray());
        Assert.Empty(body.GetProperty("recentTransactions").EnumerateArray());
    }

    [Fact]
    public async Task Today_UsesOnlyTheCurrentOwnersData()
    {
        SeedSpecExample(OwnerB);

        var body = await GetJsonAsync(_factory.CreateClientFor(OwnerA));

        Assert.Empty(body.GetProperty("currencies").EnumerateArray());
        Assert.Empty(body.GetProperty("chargesUntilMonthEnd").EnumerateArray());
    }

    [Fact]
    public async Task Today_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/dashboard/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private void SeedSpecExample(string owner)
    {
        _factory.Accounts.Items.AddRange(
        [
            Account.Create(owner, "Cash", AccountType.Cash, "COP", 200000m, Clock),
            Account.Create(owner, "Bank", AccountType.Bank, "COP", 1000000m, Clock),
            Account.Create(owner, "Visa", AccountType.CreditCard, "COP", -300000m, Clock),
        ]);
        _factory.Subscriptions.Items.Add(Subscription.Create(
            owner, "Netflix", null, 38900m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 28), Clock));
    }

    private void Record(string owner, TransactionType type, Account account, Category category, decimal amount, DateOnly date) =>
        _factory.Accounts.Transactions.Add(
            Transaction.Create(owner, type, account.Id, null, category.Id, amount, date, null, null, Clock));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard/today");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
