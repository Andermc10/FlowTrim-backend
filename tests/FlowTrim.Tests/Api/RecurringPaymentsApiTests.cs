using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>EP-14 HU-72 por HTTP: tipos, ciclos nuevos, enlace de pago, cuenta y categoría por defecto y filtro por tipo.</summary>
public sealed class RecurringPaymentsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private readonly FlowTrimApiFactory _factory;

    public RecurringPaymentsApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_WeeklyBillWithLinkAndDefaults_ReturnsEveryNewField()
    {
        var owner = NewOwner();
        var bank = AddAccount(owner, "Bank");
        var home = AddCategory(owner, "Home", CategoryKind.Expense);

        var response = await _factory.CreateClientFor(owner).PostAsJsonAsync("/api/subscriptions", Payload(new
        {
            kind = "Bill", amount = 10000m, billingCycle = "Weekly", paymentUrl = "https://pay.example.com/bill",
            defaultAccountId = bank.Id, categoryId = home.Id,
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bill", body.GetProperty("kind").GetString());
        Assert.Equal("Weekly", body.GetProperty("billingCycle").GetString());
        Assert.Equal("https://pay.example.com/bill", body.GetProperty("paymentUrl").GetString());
        Assert.Equal(bank.Id, body.GetProperty("defaultAccountId").GetGuid());
        Assert.Equal(home.Id, body.GetProperty("categoryId").GetGuid());
        Assert.Equal(43333.33m, body.GetProperty("monthlyEquivalent").GetDecimal());
    }

    [Fact]
    public async Task Create_WithoutKind_IsASubscriptionWithNullExtras()
    {
        var response = await _factory.CreateClientFor(NewOwner()).PostAsJsonAsync("/api/subscriptions", Payload(new { }));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Subscription", body.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("paymentUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("defaultAccountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("categoryId").ValueKind);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/pay")]
    [InlineData("https://user:pass@example.com/pay")]
    public async Task Create_UnsafePaymentUrl_Returns400OnPaymentUrl(string url)
    {
        // AC-2 de HU-72.
        var response = await _factory.CreateClientFor(NewOwner()).PostAsJsonAsync("/api/subscriptions", Payload(new { paymentUrl = url }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("paymentUrl", out _));
    }

    [Theory]
    [InlineData("Loan", "kind")]
    [InlineData("Daily", "billingCycle")]
    public async Task Create_UnknownKindOrCycle_Returns400OnTheField(string value, string field)
    {
        var payload = field == "kind" ? Payload(new { kind = value }) : Payload(new { billingCycle = value });

        var response = await _factory.CreateClientFor(NewOwner()).PostAsJsonAsync("/api/subscriptions", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Create_DefaultsFromAnotherOwnerOrIncomeCategory_Returns400OnEachField()
    {
        var owner = NewOwner();
        var foreignAccount = AddAccount(NewOwner(), "Foreign bank");
        var salary = AddCategory(owner, "Salary", CategoryKind.Income);

        var response = await _factory.CreateClientFor(owner).PostAsJsonAsync("/api/subscriptions", Payload(new
        {
            defaultAccountId = foreignAccount.Id, categoryId = salary.Id,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal("Account not found.", errors.GetProperty("defaultAccountId")[0].GetString());
        Assert.Equal("A recurring payment needs an expense category.", errors.GetProperty("categoryId")[0].GetString());
    }

    [Fact]
    public async Task Create_ArchivedDefaults_Returns400_ButUpdateKeepsThemWhenUnchanged()
    {
        var owner = NewOwner();
        var client = _factory.CreateClientFor(owner);
        var bank = AddAccount(owner, "Bank");
        var home = AddCategory(owner, "Home", CategoryKind.Expense);
        var created = await client.PostAsJsonAsync("/api/subscriptions", Payload(new { defaultAccountId = bank.Id, categoryId = home.Id }));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        bank.Update(bank.Name, bank.Type, bank.Currency, bank.OpeningBalance, isArchived: true, Clock);
        home.Update(home.Name, home.Kind, home.Color, home.Icon, isArchived: true, Clock);

        var newOne = await client.PostAsJsonAsync("/api/subscriptions", Payload(new { defaultAccountId = bank.Id, categoryId = home.Id }));
        var update = await client.PutAsJsonAsync($"/api/subscriptions/{id}", Payload(new
        {
            name = "Rent (renamed)", status = "Active", defaultAccountId = bank.Id, categoryId = home.Id,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, newOne.StatusCode);
        var errors = await ErrorsAsync(newOne);
        Assert.True(errors.TryGetProperty("defaultAccountId", out _));
        Assert.True(errors.TryGetProperty("categoryId", out _));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutKind_KeepsTheKind_AndMissingExtrasAreCleared()
    {
        var client = _factory.CreateClientFor(NewOwner());
        var created = await client.PostAsJsonAsync("/api/subscriptions", Payload(new { kind = "Bill", paymentUrl = "https://example.com" }));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var updated = await client.PutAsJsonAsync($"/api/subscriptions/{id}", Payload(new { status = "Active" }));

        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bill", body.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("paymentUrl").ValueKind);
    }

    [Fact]
    public async Task List_KindFilter_ReturnsOnlyThatKind_AndUnknownKindIs400()
    {
        var client = _factory.CreateClientFor(NewOwner());
        await client.PostAsJsonAsync("/api/subscriptions", Payload(new { name = "Netflix" }));
        await client.PostAsJsonAsync("/api/subscriptions", Payload(new { name = "Rent", kind = "Bill" }));
        await client.PostAsJsonAsync("/api/subscriptions", Payload(new { name = "Water", kind = "Bill" }));

        var bills = await client.GetFromJsonAsync<JsonElement>("/api/subscriptions?kind=Bill");
        var all = await client.GetFromJsonAsync<JsonElement>("/api/subscriptions");
        var unknown = await client.GetAsync("/api/subscriptions?kind=Loan");

        Assert.Equal(2, bills.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Rent", "Water"], bills.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Order());
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await ErrorsAsync(unknown)).TryGetProperty("kind", out _));
    }

    [Fact]
    public async Task MarkPaid_BiweeklyPayment_AdvancesFourteenDays()
    {
        var client = _factory.CreateClientFor(NewOwner());
        var created = await client.PostAsJsonAsync("/api/subscriptions", Payload(new { billingCycle = "Biweekly", nextBillingDate = "2026-10-25" }));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var paid = await client.PostAsync($"/api/subscriptions/{id}/mark-paid", null);

        Assert.Equal("2026-11-08", (await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subscription").GetProperty("nextBillingDate").GetString());
    }

    private string NewOwner() => $"auth0|recurring-{Guid.NewGuid():N}";

    private Account AddAccount(string owner, string name)
    {
        var account = Account.Create(owner, name, AccountType.Bank, "COP", 100000m, Clock);
        _factory.Accounts.Items.Add(account);
        return account;
    }

    private Category AddCategory(string owner, string name, CategoryKind kind)
    {
        var category = Category.Create(owner, name, kind, "orange", "home", Clock);
        _factory.Categories.Items.Add(category);
        return category;
    }

    // Pago base (factura mensual de 1.200.000 COP desde hoy) con los campos de "overrides" encima.
    private static Dictionary<string, object?> Payload(object overrides)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = "Rent",
            ["amount"] = 1200000m,
            ["currency"] = "COP",
            ["billingCycle"] = "Monthly",
            ["nextBillingDate"] = "2026-10-20",
        };
        foreach (var property in overrides.GetType().GetProperties())
        {
            payload[property.Name] = property.GetValue(overrides);
        }

        return payload;
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
}
