using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>Contrato HTTP de los movimientos (HU-66): códigos de estado, forma de la respuesta y aislamiento.</summary>
public sealed class TransactionsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|owner-a";
    private const string OwnerB = "auth0|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);

    private readonly FlowTrimApiFactory _factory;
    private readonly Account _bank;
    private readonly Category _food;

    public TransactionsApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Accounts.Items.Clear();
        _factory.Accounts.Transactions.Clear();
        _factory.Categories.Items.Clear();
        _bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 500000m, Clock);
        _food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        _factory.Accounts.Items.Add(_bank);
        _factory.Categories.Items.Add(_food);
    }

    [Fact]
    public async Task Create_Expense_Returns201AndTheAccountBalanceDrops()
    {
        var client = _factory.CreateClientFor(OwnerA);

        var response = await client.PostAsJsonAsync("/api/transactions", Expense(38900m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/transactions/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Equal("COP", body.GetProperty("currency").GetString());
        Assert.Equal("Food", body.GetProperty("categoryName").GetString());
        Assert.Equal("food", body.GetProperty("categoryIcon").GetString());
        Assert.Equal("2026-10-20", body.GetProperty("date").GetString());
        var account = await GetJsonAsync(client, $"/api/accounts/{_bank.Id}");
        Assert.Equal(461100m, account.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Create_FutureDate_Returns400WithTheSpecMessage()
    {
        var response = await _factory.CreateClientFor(OwnerA).PostAsJsonAsync(
            "/api/transactions", Expense(10m) with { date = "2026-10-21" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal("Transaction date cannot be in the future.", errors.GetProperty("date")[0].GetString());
    }

    [Fact]
    public async Task List_ReturnsPageMetadataAndTotals()
    {
        var client = _factory.CreateClientFor(OwnerA);
        await client.PostAsJsonAsync("/api/transactions", Expense(100m));
        await client.PostAsJsonAsync("/api/transactions", Expense(250m));

        var body = await GetJsonAsync(client, "/api/transactions?from=2026-10-01&to=2026-10-31&pageSize=1");

        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
        var totals = Assert.Single(body.GetProperty("totals").EnumerateArray());
        Assert.Equal("COP", totals.GetProperty("currency").GetString());
        Assert.Equal(350m, totals.GetProperty("expense").GetDecimal());
        Assert.Equal(-350m, totals.GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task List_BadType_Returns400()
    {
        var response = await _factory.CreateClientFor(OwnerA).GetAsync("/api/transactions?type=Refund");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtherOwner_CannotSeeChangeOrDeleteAndCannotUseTheAccount()
    {
        var clientA = _factory.CreateClientFor(OwnerA);
        var created = await (await clientA.PostAsJsonAsync("/api/transactions", Expense(10m))).Content.ReadFromJsonAsync<JsonElement>();
        var url = $"/api/transactions/{created.GetProperty("id").GetGuid()}";
        var clientB = _factory.CreateClientFor(OwnerB);

        var get = await clientB.GetAsync(url);
        var put = await clientB.PutAsJsonAsync(url, Expense(99m));
        var delete = await clientB.DeleteAsync(url);
        var list = await GetJsonAsync(clientB, "/api/transactions");
        var useForeignAccount = await clientB.PostAsJsonAsync("/api/transactions", Expense(5m));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, useForeignAccount.StatusCode);
        Assert.Single(_factory.Transactions.Items);
    }

    [Fact]
    public async Task UpdateAndDelete_OwnTransaction_Return200And204()
    {
        var client = _factory.CreateClientFor(OwnerA);
        var created = await (await client.PostAsJsonAsync("/api/transactions", Expense(10m))).Content.ReadFromJsonAsync<JsonElement>();
        var url = $"/api/transactions/{created.GetProperty("id").GetGuid()}";

        var put = await client.PutAsJsonAsync(url, Expense(20m) with { description = "Dinner" });
        var delete = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Dinner", (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(_factory.Transactions.Items);
    }

    private ExpenseBody Expense(decimal amount) => new("Expense", _bank.Id, _food.Id, amount, "2026-10-20", null);

    private sealed record ExpenseBody(string type, Guid accountId, Guid categoryId, decimal amount, string date, string? description);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
