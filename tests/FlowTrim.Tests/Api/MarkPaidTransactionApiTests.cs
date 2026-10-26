using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>EP-14 HU-73 por HTTP: marcar como pagado registra un gasto ligado al pago y mueve la fecha en una sola operación.</summary>
public sealed class MarkPaidTransactionApiTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private readonly FlowTrimApiFactory _factory;

    public MarkPaidTransactionApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MarkPaid_WithDefaults_RecordsTheExpenseAndMovesTheDate()
    {
        // AC-1: Netflix 38.900 con Banco por defecto → gasto de hoy ligado a Netflix, saldo −38.900, fecha +1 mes.
        var (client, owner) = NewClient();
        var bank = AddAccount(owner, "Bank", "COP");
        var subscriptions = AddCategory(owner, "Subscriptions", CategoryKind.Expense);
        var id = await CreateAsync(client, new { defaultAccountId = bank.Id, categoryId = subscriptions.Id });

        var response = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new { accountId = bank.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-11-20", body.GetProperty("subscription").GetProperty("nextBillingDate").GetString());
        Assert.False(body.GetProperty("completed").GetBoolean());
        var transaction = body.GetProperty("transaction");
        Assert.Equal("Expense", transaction.GetProperty("type").GetString());
        Assert.Equal(38900m, transaction.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-10-20", transaction.GetProperty("date").GetString());
        Assert.Equal("Netflix", transaction.GetProperty("description").GetString());
        Assert.Equal(subscriptions.Id, transaction.GetProperty("categoryId").GetGuid());
        Assert.Equal(id, transaction.GetProperty("recurringPaymentId").GetGuid());

        var account = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{bank.Id}");
        Assert.Equal(100000m - 38900m, account.GetProperty("balance").GetDecimal());
        var payments = await client.GetFromJsonAsync<JsonElement>($"/api/subscriptions/{id}/payments");
        Assert.Equal(transaction.GetProperty("id").GetGuid(), Assert.Single(payments.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MarkPaid_WithAmountDateAndCategory_UsesThem()
    {
        var (client, owner) = NewClient();
        var bank = AddAccount(owner, "Bank", "COP");
        var home = AddCategory(owner, "Home", CategoryKind.Expense);
        var id = await CreateAsync(client, new { });

        var response = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new
        {
            accountId = bank.Id, amount = 41250.5m, date = "2026-10-18", categoryId = home.Id,
        });

        var transaction = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transaction");
        Assert.Equal(41250.5m, transaction.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-10-18", transaction.GetProperty("date").GetString());
        Assert.Equal(home.Id, transaction.GetProperty("categoryId").GetGuid());
    }

    [Fact]
    public async Task MarkPaid_EmptyBody_OnlyMovesTheDate()
    {
        var (client, _) = NewClient();
        var id = await CreateAsync(client, new { });

        var response = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new { });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("transaction").ValueKind);
        Assert.Equal("2026-11-20", body.GetProperty("subscription").GetProperty("nextBillingDate").GetString());
    }

    [Fact]
    public async Task MarkPaid_InvalidFields_Returns400AndChangesNothing()
    {
        var (client, owner) = NewClient();
        var usd = AddAccount(owner, "Wallet", "USD");
        var salary = AddCategory(owner, "Salary", CategoryKind.Income);
        var foreign = AddAccount(NewClient().Owner, "Foreign", "COP");
        var id = await CreateAsync(client, new { });

        var currency = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new { accountId = usd.Id, categoryId = (Guid?)null });
        var fields = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new
        {
            accountId = foreign.Id, categoryId = salary.Id, date = "2026-10-21", amount = 0m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, currency.StatusCode);
        var currencyErrors = await ErrorsAsync(currency);
        Assert.Equal("Use an account in COP, the currency of this payment.", currencyErrors.GetProperty("accountId")[0].GetString());
        Assert.Equal("Choose an expense category for this payment.", currencyErrors.GetProperty("categoryId")[0].GetString());
        var fieldErrors = await ErrorsAsync(fields);
        Assert.Equal("Account not found.", fieldErrors.GetProperty("accountId")[0].GetString());
        Assert.Equal("An expense needs an expense category.", fieldErrors.GetProperty("categoryId")[0].GetString());
        Assert.Equal("Transaction date cannot be in the future.", fieldErrors.GetProperty("date")[0].GetString());
        var subscription = await client.GetFromJsonAsync<JsonElement>($"/api/subscriptions/{id}");
        Assert.Equal("2026-10-20", subscription.GetProperty("nextBillingDate").GetString());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/subscriptions/{id}/payments")).EnumerateArray());
    }

    [Fact]
    public async Task MarkPaid_ArchivedAccount_Returns400()
    {
        var (client, owner) = NewClient();
        var bank = AddAccount(owner, "Old bank", "COP");
        var home = AddCategory(owner, "Home", CategoryKind.Expense);
        bank.Update(bank.Name, bank.Type, bank.Currency, bank.OpeningBalance, isArchived: true, Clock);
        var id = await CreateAsync(client, new { categoryId = home.Id });

        var response = await client.PostAsJsonAsync($"/api/subscriptions/{id}/mark-paid", new { accountId = bank.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("accountId", out _));
    }

    [Fact]
    public async Task Payments_ForeignOrMissingPayment_Returns404()
    {
        var (owner, _) = NewClient();
        var (stranger, _) = NewClient();
        var id = await CreateAsync(owner, new { });

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/subscriptions/{id}/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/subscriptions/{Guid.NewGuid()}/payments")).StatusCode);
    }

    private (HttpClient Client, string Owner) NewClient()
    {
        var owner = $"auth0|mark-paid-{Guid.NewGuid():N}";
        return (_factory.CreateClientFor(owner), owner);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, object overrides)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = "Netflix", ["amount"] = 38900m, ["currency"] = "COP", ["billingCycle"] = "Monthly",
            ["nextBillingDate"] = "2026-10-20",
        };
        foreach (var property in overrides.GetType().GetProperties())
        {
            payload[property.Name] = property.GetValue(overrides);
        }

        var response = await client.PostAsJsonAsync("/api/subscriptions", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Account AddAccount(string owner, string name, string currency)
    {
        var account = Account.Create(owner, name, AccountType.Bank, currency, 100000m, Clock);
        _factory.Accounts.Items.Add(account);
        return account;
    }

    private Category AddCategory(string owner, string name, CategoryKind kind)
    {
        var category = Category.Create(owner, name, kind, "orange", "home", Clock);
        _factory.Categories.Items.Add(category);
        return category;
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
}
