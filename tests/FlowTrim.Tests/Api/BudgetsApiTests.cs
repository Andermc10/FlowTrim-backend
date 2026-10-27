using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>EP-15 HU-76 por HTTP: presupuestos, avance del mes, alertas en Hoy, validación y aislamiento.</summary>
public sealed class BudgetsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private readonly FlowTrimApiFactory _factory;

    public BudgetsApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Budget_SpendingThisMonth_MovesFromOnTrackToWarningToExceededAndShowsOnToday()
    {
        // AC-1 y AC-2: Comida 400.000 COP; solo cuentan los gastos de Comida en COP de octubre.
        var owner = NewOwner();
        var client = _factory.CreateClientFor(owner);
        var (bank, food) = Seed(owner);
        var dollars = AddAccount(owner, "Dollars", "USD");
        var transport = AddCategory(owner, "Transport", CategoryKind.Expense);
        var created = await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "cop", limit = 400000m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Expense(owner, bank, food, 250000m, new DateOnly(2026, 10, 5));
        Expense(owner, dollars, food, 50m, new DateOnly(2026, 10, 6));
        Expense(owner, bank, transport, 99999m, new DateOnly(2026, 10, 7));
        Expense(owner, bank, food, 99999m, new DateOnly(2026, 9, 30));

        var onTrack = Single(await client.GetFromJsonAsync<JsonElement>("/api/budgets"));
        Expense(owner, bank, food, 80000m, new DateOnly(2026, 10, 10));
        var warning = Single(await client.GetFromJsonAsync<JsonElement>("/api/budgets"));
        var today = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/today");
        Expense(owner, bank, food, 100000m, new DateOnly(2026, 10, 12));
        var exceeded = Single(await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10"));

        Assert.Equal(250000m, onTrack.GetProperty("spent").GetDecimal());
        Assert.Equal(150000m, onTrack.GetProperty("remaining").GetDecimal());
        Assert.Equal(62.5m, onTrack.GetProperty("percent").GetDecimal());
        Assert.Equal("OnTrack", onTrack.GetProperty("state").GetString());
        Assert.Equal("Warning", warning.GetProperty("state").GetString());
        Assert.Equal(82.5m, warning.GetProperty("percent").GetDecimal());
        Assert.Equal("Food", Assert.Single(today.GetProperty("budgetsToWatch").EnumerateArray()).GetProperty("categoryName").GetString());
        Assert.Equal("Exceeded", exceeded.GetProperty("state").GetString());
        Assert.Equal(30000m, exceeded.GetProperty("over").GetDecimal());
        Assert.Equal("COP", exceeded.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Budget_PreviousMonth_OnlyCountsThatMonth()
    {
        var owner = NewOwner();
        var client = _factory.CreateClientFor(owner);
        var (bank, food) = Seed(owner);
        await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "COP", limit = 100m });
        Expense(owner, bank, food, 90m, new DateOnly(2026, 9, 15));

        var september = await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-09");
        var october = await client.GetFromJsonAsync<JsonElement>("/api/budgets");

        Assert.Equal("2026-09", september.GetProperty("month").GetString());
        Assert.Equal(90m, Single(september).GetProperty("spent").GetDecimal());
        Assert.Equal(0m, Single(october).GetProperty("spent").GetDecimal());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/dashboard/today")).GetProperty("budgetsToWatch").EnumerateArray());
    }

    [Fact]
    public async Task Create_Duplicate_Returns409_AndInvalidInputsReturn400OnTheirFields()
    {
        var owner = NewOwner();
        var client = _factory.CreateClientFor(owner);
        var (_, food) = Seed(owner);
        var salary = AddCategory(owner, "Salary", CategoryKind.Income);
        var foreign = AddCategory(NewOwner(), "Food", CategoryKind.Expense);
        await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "COP", limit = 100m });

        var duplicate = await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "COP", limit = 200m });
        var income = await client.PostAsJsonAsync("/api/budgets", new { categoryId = salary.Id, currency = "COP", limit = 100m });
        var stranger = await client.PostAsJsonAsync("/api/budgets", new { categoryId = foreign.Id, currency = "COP", limit = 100m });
        var limit = await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "USD", limit = 0m });
        var month = await client.GetAsync("/api/budgets?month=2026-13");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("budget_exists", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("A budget needs an expense category.", (await ErrorsAsync(income)).GetProperty("categoryId")[0].GetString());
        Assert.Equal("Category not found.", (await ErrorsAsync(stranger)).GetProperty("categoryId")[0].GetString());
        Assert.True((await ErrorsAsync(limit)).TryGetProperty("limit", out _));
        Assert.True((await ErrorsAsync(month)).TryGetProperty("month", out _));
    }

    [Fact]
    public async Task UpdateAndDelete_OwnBudget_Work_AndAnotherOwnersBudgetIs404()
    {
        var owner = NewOwner();
        var client = _factory.CreateClientFor(owner);
        var stranger = _factory.CreateClientFor(NewOwner());
        var (_, food) = Seed(owner);
        var id = (await (await client.PostAsJsonAsync("/api/budgets", new { categoryId = food.Id, currency = "COP", limit = 100m }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var foreignUpdate = await stranger.PutAsJsonAsync($"/api/budgets/{id}", new { limit = 1m });
        var updated = await client.PutAsJsonAsync($"/api/budgets/{id}", new { limit = 250m });
        var foreignDelete = await stranger.DeleteAsync($"/api/budgets/{id}");
        var deleted = await client.DeleteAsync($"/api/budgets/{id}");

        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);
        Assert.Equal(250m, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("limit").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/budgets")).GetProperty("items").EnumerateArray());
    }

    private static string NewOwner() => $"auth0|budgets-{Guid.NewGuid():N}";

    private (Account Bank, Category Food) Seed(string owner) =>
        (AddAccount(owner, "Bank", "COP"), AddCategory(owner, "Food", CategoryKind.Expense));

    private Account AddAccount(string owner, string name, string currency)
    {
        var account = Account.Create(owner, name, AccountType.Bank, currency, 0m, Clock);
        _factory.Accounts.Items.Add(account);
        return account;
    }

    private Category AddCategory(string owner, string name, CategoryKind kind)
    {
        var category = Category.Create(owner, name, kind, "orange", "food", Clock);
        _factory.Categories.Items.Add(category);
        return category;
    }

    private void Expense(string owner, Account account, Category category, decimal amount, DateOnly date) =>
        _factory.Accounts.Transactions.Add(
            Transaction.Create(owner, TransactionType.Expense, account.Id, null, category.Id, amount, date, null, null, Clock));

    private static JsonElement Single(JsonElement list) => Assert.Single(list.GetProperty("items").EnumerateArray());

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
}
