using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>Contrato HTTP de las cuentas (HU-64): códigos de estado, 409 con <c>code</c> y aislamiento por propietario.</summary>
public sealed class AccountsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|owner-a";
    private const string OwnerB = "auth0|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);

    private readonly FlowTrimApiFactory _factory;

    public AccountsApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Accounts.Items.Clear();
        _factory.Accounts.Transactions.Clear();
    }

    [Fact]
    public async Task Create_ValidAccount_Returns201WithLocationAndBalance()
    {
        var response = await _factory.CreateClientFor(OwnerA).PostAsJsonAsync("/api/accounts", new
        {
            name = "Bank",
            type = "Bank",
            currency = "COP",
            openingBalance = 500000m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/accounts/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Equal(500000m, body.GetProperty("balance").GetDecimal());
        Assert.Equal("Bank", body.GetProperty("type").GetString());
        Assert.False(body.GetProperty("hasTransactions").GetBoolean());
        Assert.Equal(OwnerA, Assert.Single(_factory.Accounts.Items).OwnerId);
    }

    [Fact]
    public async Task Create_InvalidFields_Returns400WithFieldErrors()
    {
        var response = await _factory.CreateClientFor(OwnerA).PostAsJsonAsync("/api/accounts", new
        {
            name = "Bank",
            type = "bank",
            currency = "COP",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("type", out _));
        Assert.True(errors.TryGetProperty("openingBalance", out _));
    }

    [Fact]
    public async Task Create_InvalidCurrency_Returns400FromDomain()
    {
        var response = await _factory.CreateClientFor(OwnerA).PostAsJsonAsync("/api/accounts", new
        {
            name = "Bank",
            type = "Bank",
            currency = "PESOS",
            openingBalance = 0m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("currency", out _));
    }

    [Fact]
    public async Task List_ReturnsComputedBalanceAndHidesArchivedByDefault()
    {
        var bank = Seed(OwnerA, "Bank", 500000m);
        var archived = Seed(OwnerA, "Old wallet", 0m);
        archived.Update("Old wallet", AccountType.Cash, "COP", 0m, isArchived: true, Clock);
        _factory.Accounts.Transactions.Add(Transaction.Create(
            OwnerA, TransactionType.Expense, bank.Id, null, Guid.NewGuid(), 38900m, new DateOnly(2026, 10, 20), null, null, Clock));
        var client = _factory.CreateClientFor(OwnerA);

        var active = await GetJsonAsync(client, "/api/accounts");
        var all = await GetJsonAsync(client, "/api/accounts?includeArchived=true");

        var account = Assert.Single(active.EnumerateArray());
        Assert.Equal(461100m, account.GetProperty("balance").GetDecimal());
        Assert.True(account.GetProperty("hasTransactions").GetBoolean());
        Assert.Equal(2, all.GetArrayLength());
    }

    [Fact]
    public async Task Delete_AccountWithTransactions_Returns409WithCode()
    {
        var bank = Seed(OwnerA, "Bank", 0m);
        _factory.Accounts.Transactions.Add(Transaction.Create(
            OwnerA, TransactionType.Expense, bank.Id, null, Guid.NewGuid(), 10m, new DateOnly(2026, 10, 20), null, null, Clock));

        var response = await _factory.CreateClientFor(OwnerA).DeleteAsync($"/api/accounts/{bank.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("account_has_transactions", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("correlationId", out _));
        Assert.Contains(bank, _factory.Accounts.Items);
    }

    [Fact]
    public async Task Update_CurrencyOfUsedAccount_Returns409WithCode()
    {
        var bank = Seed(OwnerA, "Bank", 0m);
        _factory.Accounts.Transactions.Add(Transaction.Create(
            OwnerA, TransactionType.Income, bank.Id, null, Guid.NewGuid(), 10m, new DateOnly(2026, 10, 20), null, null, Clock));

        var response = await _factory.CreateClientFor(OwnerA).PutAsJsonAsync($"/api/accounts/{bank.Id}", new
        {
            name = "Bank",
            type = "Bank",
            currency = "USD",
            openingBalance = 0m,
            isArchived = false,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("account_currency_locked", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_UnusedAccount_Returns204()
    {
        var bank = Seed(OwnerA, "Bank", 0m);

        var response = await _factory.CreateClientFor(OwnerA).DeleteAsync($"/api/accounts/{bank.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.Accounts.Items);
    }

    [Fact]
    public async Task OtherOwnersAccount_EveryRoute_Returns404AndListHidesIt()
    {
        // AC-3 de HU-64.
        var foreign = Seed(OwnerB, "Owner B bank", 900m);
        var client = _factory.CreateClientFor(OwnerA);
        var url = $"/api/accounts/{foreign.Id}";

        var get = await client.GetAsync(url);
        var put = await client.PutAsJsonAsync(url, new
        {
            name = "Changed",
            type = "Bank",
            currency = "COP",
            openingBalance = 0m,
            isArchived = false,
        });
        var delete = await client.DeleteAsync(url);
        var list = await GetJsonAsync(client, "/api/accounts?includeArchived=true");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(0, list.GetArrayLength());
        Assert.Equal("Owner B bank", foreign.Name);
        Assert.Contains(foreign, _factory.Accounts.Items);
    }

    private Account Seed(string owner, string name, decimal openingBalance)
    {
        var account = Account.Create(owner, name, AccountType.Bank, "COP", openingBalance, Clock);
        _factory.Accounts.Items.Add(account);
        return account;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
