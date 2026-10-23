using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Tests.Api;

namespace FlowTrim.Tests.Integration.Api;

/// <summary>
/// Ciclo diario de EP-12 por HTTP sobre PostgreSQL real: cuentas → categorías por defecto → suscripción → gasto y
/// transferencia → panel "Hoy". Comprueba que los totales sumados en SQL alimentan bien "Puedes gastar hoy".
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyFinanceApiTests : IAsyncLifetime
{
    private const string Owner = "it|daily-owner";

    private readonly PostgresFixture _database;
    private readonly FlowTrimApiFactory _factory;

    public DailyFinanceApiTests(PostgresFixture database)
    {
        _database = database;
        // Reloj por defecto de la factoría: 2026-10-20 en Bogotá (día 20 de un mes de 31 días, como el AC-1).
        _factory = new FlowTrimApiFactory { DatabaseConnectionString = database.ConnectionString };
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task DailyLoop_RealDatabase_TodayMatchesTheSpecFormula()
    {
        var client = _factory.CreateClientFor(Owner);
        var cash = await CreateAccountAsync(client, "Cash", "Cash", 200000m);
        var bank = await CreateAccountAsync(client, "Bank", "Bank", 1000000m);
        await CreateAccountAsync(client, "Visa", "CreditCard", -300000m);
        var subscription = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            name = "Netflix", amount = 38900m, currency = "COP", billingCycle = "Monthly", nextBillingDate = "2026-10-28",
        });
        Assert.Equal(HttpStatusCode.Created, subscription.StatusCode);

        // AC-1 de HU-68 sin movimientos.
        var before = Single(await GetJsonAsync(client, "/api/dashboard/today"));
        Assert.Equal(1200000m, before.GetProperty("available").GetDecimal());
        Assert.Equal(96758.33m, before.GetProperty("safeToSpendToday").GetDecimal());

        // La primera lista crea las categorías por defecto; se usa "Food" para un gasto en efectivo de hoy.
        var categories = await GetJsonAsync(client, "/api/categories?kind=Expense");
        var food = categories.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Food").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/transactions", new
        {
            type = "Expense", accountId = cash, categoryId = food, amount = 10000m, date = "2026-10-20",
        })).StatusCode);
        // Una transferencia mueve dinero entre cuentas propias: no cambia el disponible ni cuenta como gasto.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/transactions", new
        {
            type = "Transfer", accountId = bank, toAccountId = cash, amount = 100000m, date = "2026-10-20",
        })).StatusCode);

        var dashboard = await GetJsonAsync(client, "/api/dashboard/today");
        var cop = Single(dashboard);
        Assert.Equal(1190000m, cop.GetProperty("available").GetDecimal());
        Assert.Equal(10000m, cop.GetProperty("todayExpenses").GetDecimal());
        Assert.Equal(10000m, cop.GetProperty("monthExpenses").GetDecimal());
        Assert.Equal(0m, cop.GetProperty("monthIncome").GetDecimal());
        // (1.190.000 − 38.900) / 12 = 95.925
        Assert.Equal(95925m, cop.GetProperty("safeToSpendToday").GetDecimal());
        Assert.Equal(2, dashboard.GetProperty("recentTransactions").GetArrayLength());
        var balances = dashboard.GetProperty("accounts").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("balance").GetDecimal());
        Assert.Equal(290000m, balances["Cash"]);
        Assert.Equal(900000m, balances["Bank"]);
        Assert.Equal(-300000m, balances["Visa"]);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name, string type, decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { name, type, currency = "COP", openingBalance });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static JsonElement Single(JsonElement dashboard) => Assert.Single(dashboard.GetProperty("currencies").EnumerateArray());

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
