using System.Text.Json;
using FlowTrim.Application.Analytics;
using FlowTrim.Application.Insights;
using FlowTrim.Application.Insights.Tools;
using FlowTrim.Application.Simulations;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Summaries;
using FlowTrim.Application.Usage;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Insights.Tools;

/// <summary>HU-34: herramientas financieras con los handlers reales y repositorios en memoria.</summary>
public sealed class FinancialToolsTests
{
    private const string OwnerA = "auth0|tools-a";
    private const string OwnerB = "auth0|tools-b";

    // 17:00 UTC = 12:00 en Bogotá: la fecha de referencia del producto es 2026-10-19.
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 19, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly CountingSubscriptionRepository _subscriptions = new();
    private readonly InMemoryUsageRecordRepository _usage = new();
    private readonly FixedTimeProvider _clock = new(FixedNow);

    private static readonly string[] ExpectedToolNames =
    [
        "get_spending_summary",
        "get_spending_analytics",
        "get_upcoming_charges",
        "simulate_cancellation",
        "get_cost_per_use",
    ];

    // ----- Registro (allow-list) -----

    [Fact]
    public void Registry_ExposesExactlyTheFiveToolsWithObjectSchemas()
    {
        var definitions = RegistryFor(OwnerA).Definitions;

        Assert.Equal(ExpectedToolNames.Order(), definitions.Select(definition => definition.Name).Order());
        Assert.All(definitions, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            using var schema = JsonDocument.Parse(definition.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.RootElement.GetProperty("properties").ValueKind);
        });
    }

    [Fact]
    public async Task Registry_UnknownTool_ReturnsUnknownTool()
    {
        var result = await RegistryFor(OwnerA).ExecuteAsync(new ChatToolCall("call-1", "delete_subscription", "{}"), default);

        Assert.Equal(ToolStatus.UnknownTool, result.Status);
        Assert.Equal("delete_subscription", result.Tool);
        Assert.Equal(0, _subscriptions.ReadCount);
    }

    // ----- get_spending_summary -----

    [Fact]
    public async Task SummaryTool_MixedCurrencies_MatchesSummaryHandler()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerA, "Cloud", 120m, "USD", BillingCycle.Yearly);
        Seed(OwnerB, "Gym", 90000m, "COP");

        var json = await RunAsync(OwnerA, "get_spending_summary", "{}");
        var expected = await new GetSummaryHandler(_subscriptions, new TestCurrentUser(OwnerA)).HandleAsync(default);

        Assert.Equal("ok", json.GetProperty("status").GetString());
        var currencies = json.GetProperty("data").GetProperty("currencies").EnumerateArray().ToList();
        Assert.Equal(expected.Currencies.Count, currencies.Count);
        foreach (var (actual, handler) in currencies.Zip(expected.Currencies))
        {
            Assert.Equal(handler.Currency, actual.GetProperty("currency").GetString());
            Assert.Equal(handler.MonthlyEquivalent, actual.GetProperty("monthlyEquivalent").GetDecimal());
            Assert.Equal(handler.AnnualProjection, actual.GetProperty("annualProjection").GetDecimal());
            Assert.Equal(handler.ActiveCount, actual.GetProperty("activeCount").GetInt32());
        }

        Assert.Equal(38900m, currencies.Single(c => c.GetProperty("currency").GetString() == "COP").GetProperty("monthlyEquivalent").GetDecimal());
    }

    [Fact]
    public async Task SummaryTool_NoSubscriptions_ReturnsOkWithEmptyData()
    {
        var json = await RunAsync(OwnerA, "get_spending_summary", null);

        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal(0, json.GetProperty("data").GetProperty("currencies").GetArrayLength());
    }

    // ----- get_spending_analytics -----

    [Theory]
    [InlineData("""{"months":0}""")]
    [InlineData("""{"months":25}""")]
    [InlineData("""{"months":"12"}""")]
    [InlineData("""{"months":1.5}""")]
    public async Task AnalyticsTool_InvalidMonths_ReturnsInvalidArguments(string arguments)
    {
        var json = await RunAsync(OwnerA, "get_spending_analytics", arguments);

        Assert.Equal("invalid_arguments", json.GetProperty("status").GetString());
        Assert.Contains("between 1 and 24", json.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(0, _subscriptions.ReadCount);
    }

    [Fact]
    public async Task AnalyticsTool_DefaultMonths_ReturnsTwelveMonthsWithNamesOnly()
    {
        var netflix = Seed(OwnerA, "Netflix", 38900m, "COP", category: "Streaming");

        var json = await RunAsync(OwnerA, "get_spending_analytics", """{"months":null}""");

        var data = json.GetProperty("data");
        Assert.Equal(12, data.GetProperty("months").GetInt32());
        var cop = Assert.Single(data.GetProperty("currencies").EnumerateArray());
        Assert.Equal(12, cop.GetProperty("expectedCharges").GetArrayLength());
        var top = Assert.Single(cop.GetProperty("topSubscriptions").EnumerateArray());
        Assert.Equal("Netflix", top.GetProperty("name").GetString());
        Assert.False(top.TryGetProperty("id", out _));
        Assert.DoesNotContain(netflix.Id.ToString(), json.GetRawText());
    }

    // ----- get_upcoming_charges -----

    [Fact]
    public async Task UpcomingTool_FiltersByWindowAndCapsItems()
    {
        Seed(OwnerA, "Overdue", 10m, "COP", next: Today.AddDays(-2));
        for (var index = 0; index < 21; index++)
        {
            Seed(OwnerA, $"Soon {index:D2}", 10m, "COP", next: Today.AddDays(5));
        }

        Seed(OwnerA, "Later", 10m, "COP", next: Today.AddDays(40));

        var json = await RunAsync(OwnerA, "get_upcoming_charges", """{"withinDays":30}""");

        var data = json.GetProperty("data");
        Assert.Equal(22, data.GetProperty("totalCount").GetInt32());
        var charges = data.GetProperty("charges").EnumerateArray().ToList();
        Assert.Equal(UpcomingChargesTool.MaxItems, charges.Count);
        Assert.Equal("Overdue", charges[0].GetProperty("name").GetString());
        Assert.Equal(-2, charges[0].GetProperty("daysUntilCharge").GetInt32());
        Assert.Equal("Overdue", charges[0].GetProperty("alertWindow").GetString());
        Assert.DoesNotContain(charges, charge => charge.GetProperty("name").GetString() == "Later");
        Assert.All(charges, charge => Assert.False(charge.TryGetProperty("id", out _)));
    }

    [Fact]
    public async Task UpcomingTool_DefaultWindow_IsThirtyDays()
    {
        Seed(OwnerA, "Inside", 10m, "COP", next: Today.AddDays(30));
        Seed(OwnerA, "Outside", 10m, "COP", next: Today.AddDays(31));

        var json = await RunAsync(OwnerA, "get_upcoming_charges", "");

        var data = json.GetProperty("data");
        Assert.Equal(30, data.GetProperty("withinDays").GetInt32());
        Assert.Equal("Inside", Assert.Single(data.GetProperty("charges").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpcomingTool_ReturnsOwnerRowsOnly()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP", next: Today.AddDays(3));
        Seed(OwnerB, "Gym", 90000m, "COP", next: Today.AddDays(3));

        var json = await RunAsync(OwnerA, "get_upcoming_charges", "{}");

        var charge = Assert.Single(json.GetProperty("data").GetProperty("charges").EnumerateArray());
        Assert.Equal("Netflix", charge.GetProperty("name").GetString());
        Assert.DoesNotContain("Gym", json.GetRawText());
    }

    // ----- simulate_cancellation -----

    [Fact]
    public async Task SimulationTool_KnownAndUnknownNames_ReportsUnmatched()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerA, "Spotify", 16900m, "COP");

        var json = await RunAsync(OwnerA, "simulate_cancellation", """{"subscriptionNames":[" netflix ","Disney"]}""");

        var data = json.GetProperty("data");
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal(["Netflix"], data.GetProperty("selectedNames").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal(["Disney"], data.GetProperty("unmatchedNames").EnumerateArray().Select(n => n.GetString()));
        var cop = Assert.Single(data.GetProperty("currencies").EnumerateArray());
        Assert.Equal(55800m, cop.GetProperty("currentMonthly").GetDecimal());
        Assert.Equal(38900m, cop.GetProperty("selectedMonthly").GetDecimal());
        var savings = cop.GetProperty("savings").EnumerateArray().ToDictionary(
            s => s.GetProperty("months").GetInt32(), s => s.GetProperty("amount").GetDecimal());
        Assert.Equal(38900m, savings[1]);
        Assert.Equal(466800m, savings[12]);
        Assert.Equal(933600m, savings[24]);
    }

    [Fact]
    public async Task SimulationTool_OnlySendsCurrenciesAffectedBySelection()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerA, "Cloud", 10m, "USD");

        var json = await RunAsync(OwnerA, "simulate_cancellation", """{"subscriptionNames":["Netflix"]}""");

        // HU-37: con USD en los datos el modelo escribía "0 USD" aunque esa moneda no tenía nada seleccionado.
        var currency = Assert.Single(json.GetProperty("data").GetProperty("currencies").EnumerateArray());
        Assert.Equal("COP", currency.GetProperty("currency").GetString());
        Assert.DoesNotContain("USD", json.GetRawText());
    }

    [Fact]
    public async Task SimulationTool_CustomHorizons_AreUsed()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");

        var json = await RunAsync(OwnerA, "simulate_cancellation", """{"subscriptionNames":["Netflix"],"horizons":[6]}""");

        var cop = Assert.Single(json.GetProperty("data").GetProperty("currencies").EnumerateArray());
        var saving = Assert.Single(cop.GetProperty("savings").EnumerateArray());
        Assert.Equal(6, saving.GetProperty("months").GetInt32());
        Assert.Equal(233400m, saving.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task SimulationTool_OtherOwnerName_DoesNotLeakData()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerA, "Old gym", 50000m, "COP", active: false);
        Seed(OwnerB, "Gym", 90000m, "USD");

        var json = await RunAsync(OwnerA, "simulate_cancellation", """{"subscriptionNames":["Gym","Old gym"]}""");

        var data = json.GetProperty("data");
        Assert.Empty(data.GetProperty("selectedNames").EnumerateArray());
        Assert.Equal(["Gym", "Old gym"], data.GetProperty("unmatchedNames").EnumerateArray().Select(n => n.GetString()));
        // Sin coincidencias ninguna moneda se ve afectada: no se envían ceros que el modelo pueda narrar.
        Assert.Empty(data.GetProperty("currencies").EnumerateArray());
        Assert.DoesNotContain("90000", json.GetRawText());
        Assert.DoesNotContain("USD", json.GetRawText());
    }

    // ----- get_cost_per_use -----

    [Fact]
    public async Task CostPerUseTool_ZeroUsage_ReturnsNotCalculable()
    {
        var netflix = Seed(OwnerA, "Netflix", 10m, "COP");
        _usage.Items.Add(UsageRecord.Create(netflix.Id, new DateOnly(2026, 10, 10), 0, null));

        var json = await RunAsync(OwnerA, "get_cost_per_use",
            """{"subscriptionName":"NETFLIX","from":"2026-10-01","to":"2026-10-31"}""");

        Assert.Equal("not_calculable", json.GetProperty("status").GetString());
        var data = json.GetProperty("data");
        Assert.Equal("No usage in the selected period.", data.GetProperty("reason").GetString());
        Assert.Equal(0, data.GetProperty("totalUses").GetInt64());
        Assert.False(data.TryGetProperty("costPerUse", out _));
    }

    [Fact]
    public async Task CostPerUseTool_WithUsage_ReturnsHandlerFigures()
    {
        var netflix = Seed(OwnerA, "Netflix", 10m, "COP");
        _usage.Items.Add(UsageRecord.Create(netflix.Id, new DateOnly(2026, 10, 10), 4, null));

        var json = await RunAsync(OwnerA, "get_cost_per_use",
            """{"subscriptionName":"Netflix","from":"2026-10-01","to":"2026-10-31"}""");

        Assert.Equal("ok", json.GetProperty("status").GetString());
        var data = json.GetProperty("data");
        Assert.Equal("Netflix", data.GetProperty("subscriptionName").GetString());
        Assert.Equal(10m, data.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(2.50m, data.GetProperty("costPerUse").GetDecimal());
        Assert.DoesNotContain(netflix.Id.ToString(), json.GetRawText());
    }

    [Fact]
    public async Task CostPerUseTool_InactiveSubscription_IsStillFound()
    {
        Seed(OwnerA, "Old gym", 50000m, "COP", active: false);

        var json = await RunAsync(OwnerA, "get_cost_per_use", """{"subscriptionName":"Old gym"}""");

        Assert.Equal("not_calculable", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CostPerUseTool_AmbiguousName_ReturnsCandidatesWithoutIds()
    {
        var first = Seed(OwnerA, "Spotify", 16900m, "COP");
        var second = Seed(OwnerA, "spotify", 11m, "USD");

        var json = await RunAsync(OwnerA, "get_cost_per_use", """{"subscriptionName":"Spotify"}""");

        Assert.Equal("ambiguous", json.GetProperty("status").GetString());
        var candidates = json.GetProperty("data").GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(2, candidates.Count);
        Assert.Equal(["COP", "USD"], candidates.Select(c => c.GetProperty("currency").GetString()).Order());
        Assert.All(candidates, candidate => Assert.False(candidate.TryGetProperty("id", out _)));
        Assert.DoesNotContain(first.Id.ToString(), json.GetRawText());
        Assert.DoesNotContain(second.Id.ToString(), json.GetRawText());
    }

    [Fact]
    public async Task CostPerUseTool_OtherOwnerName_ReturnsNotFound()
    {
        var gym = Seed(OwnerB, "Gym", 90000m, "COP");
        _usage.Items.Add(UsageRecord.Create(gym.Id, new DateOnly(2026, 10, 10), 3, null));

        var json = await RunAsync(OwnerA, "get_cost_per_use", """{"subscriptionName":"Gym"}""");

        Assert.Equal("not_found", json.GetProperty("status").GetString());
        Assert.DoesNotContain("90000", json.GetRawText());
    }

    // ----- Argumentos inválidos (AC-5) -----

    [Theory]
    [InlineData("get_spending_summary", "not json")]
    [InlineData("get_spending_analytics", "[1]")]
    [InlineData("get_upcoming_charges", """{"withinDays":90}""")]
    [InlineData("get_upcoming_charges", """{"withinDays":0}""")]
    [InlineData("simulate_cancellation", "{}")]
    [InlineData("simulate_cancellation", """{"subscriptionNames":[]}""")]
    [InlineData("simulate_cancellation", """{"subscriptionNames":["  "]}""")]
    [InlineData("simulate_cancellation", """{"subscriptionNames":"Netflix"}""")]
    [InlineData("simulate_cancellation", """{"subscriptionNames":["1","2","3","4","5","6","7","8","9","10","11"]}""")]
    [InlineData("simulate_cancellation", """{"subscriptionNames":["Netflix"],"horizons":[0]}""")]
    [InlineData("get_cost_per_use", "{}")]
    [InlineData("get_cost_per_use", """{"subscriptionName":"Netflix","from":"2026/10/01"}""")]
    [InlineData("get_cost_per_use", """{"subscriptionName":"Netflix","from":"2026-10-31","to":"2026-10-01"}""")]
    public async Task Tools_InvalidJsonArguments_ReturnInvalidArguments(string tool, string arguments)
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");

        var json = await RunAsync(OwnerA, tool, arguments);

        Assert.Equal("invalid_arguments", json.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("data").GetProperty("reason").GetString()));
        Assert.Equal(0, _subscriptions.ReadCount);
    }

    [Fact]
    public async Task UpcomingTool_OutOfRangeWindow_ExplainsTheRange()
    {
        var json = await RunAsync(OwnerA, "get_upcoming_charges", """{"withinDays":90}""");

        Assert.Equal("withinDays must be an integer between 1 and 60.", json.GetProperty("data").GetProperty("reason").GetString());
    }

    // ----- Supuestos, fuentes y forma del JSON -----

    [Theory]
    [InlineData("get_spending_summary", "{}", new[] { ToolAssumptions.Expected, ToolAssumptions.ConstantPrices, ToolAssumptions.NoConversion })]
    [InlineData("get_spending_analytics", "{}", new[] { ToolAssumptions.Expected, ToolAssumptions.NoConversion })]
    [InlineData("get_upcoming_charges", "{}", new[] { ToolAssumptions.Expected })]
    [InlineData("simulate_cancellation", """{"subscriptionNames":["Netflix"]}""", new[] { ToolAssumptions.NothingCancelled, ToolAssumptions.ConstantPrices })]
    [InlineData("get_cost_per_use", """{"subscriptionName":"Netflix"}""", new[] { ToolAssumptions.DeclaredUsage })]
    public async Task Tools_Results_CarryAssumptionsAndSources(string tool, string arguments, string[] expectedAssumptions)
    {
        Seed(OwnerA, "Netflix", 38900m, "COP", next: Today.AddDays(3));

        var result = await RegistryFor(OwnerA).ExecuteAsync(new ChatToolCall("call-1", tool, arguments), default);

        Assert.Equal(tool, result.Tool);
        Assert.All(expectedAssumptions, assumption => Assert.Contains(assumption, result.Assumptions));
        Assert.NotEmpty(result.Sources);
    }

    [Fact]
    public async Task ToolResult_ToJson_IsCompactCamelCase()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");

        var result = await RegistryFor(OwnerA).ExecuteAsync(
            new ChatToolCall("call-1", "get_cost_per_use", """{"subscriptionName":"Netflix"}"""), default);
        var text = result.ToJson();

        Assert.DoesNotContain('\n', text);
        Assert.StartsWith("""{"tool":"get_cost_per_use","status":"not_calculable","data":{""", text);
        Assert.Contains("\"subscriptionName\":\"Netflix\"", text);
        Assert.Contains("\"assumptions\":[", text);
        Assert.Contains("\"sources\":[", text);
        Assert.DoesNotContain("\"costPerUse\"", text);
        Assert.DoesNotContain("SubscriptionName", text);
    }

    // ----- Helpers -----

    private FinancialToolRegistry RegistryFor(string owner) => TestFinancialTools.Create(_subscriptions, _usage, _clock, owner);

    private async Task<JsonElement> RunAsync(string owner, string tool, string? arguments)
    {
        var result = await RegistryFor(owner).ExecuteAsync(new ChatToolCall("call-1", tool, arguments ?? ""), default);
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }

    private Subscription Seed(
        string owner,
        string name,
        decimal amount,
        string currency,
        BillingCycle cycle = BillingCycle.Monthly,
        DateOnly? next = null,
        string? category = null,
        bool active = true)
    {
        var subscription = Subscription.Create(owner, name, category, amount, currency, cycle, next ?? Today.AddDays(10), _clock);
        if (!active)
        {
            subscription.Update(name, category, amount, currency, cycle, next ?? Today.AddDays(10), SubscriptionStatus.Inactive, _clock);
        }

        _subscriptions.Inner.Items.Add(subscription);
        return subscription;
    }

    /// <summary>Envuelve el repositorio en memoria y cuenta lecturas, para probar que un argumento inválido no consulta datos.</summary>
    private sealed class CountingSubscriptionRepository : ISubscriptionRepository
    {
        public InMemorySubscriptionRepository Inner { get; } = new();

        public int ReadCount { get; private set; }

        public Task AddAsync(Subscription subscription, CancellationToken cancellationToken) =>
            Inner.AddAsync(subscription, cancellationToken);

        public Task<Subscription?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Inner.GetByIdAsync(ownerId, id, cancellationToken);
        }

        public Task<IReadOnlyList<Subscription>> ListAsync(
            string ownerId, SubscriptionKind? kind, int skip, int take, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Inner.ListAsync(ownerId, kind, skip, take, cancellationToken);
        }

        public Task<int> CountAsync(string ownerId, SubscriptionKind? kind, CancellationToken cancellationToken) =>
            Inner.CountAsync(ownerId, kind, cancellationToken);

        public Task AddImportedAsync(string ownerId, Func<IReadOnlyList<Subscription>, IReadOnlyList<Subscription>> selectNew,
            CancellationToken cancellationToken) => Inner.AddImportedAsync(ownerId, selectNew, cancellationToken);

        public Task<IReadOnlyList<Subscription>> ListActiveAsync(string ownerId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Inner.ListActiveAsync(ownerId, cancellationToken);
        }

        public Task<IReadOnlyList<Subscription>> ListAllAsync(string ownerId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Inner.ListAllAsync(ownerId, cancellationToken);
        }

        public Task RecordPaymentAsync(
            Subscription subscription, Domain.Transactions.Transaction payment, CancellationToken cancellationToken) =>
            Inner.RecordPaymentAsync(subscription, payment, cancellationToken);

        public Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken) =>
            Inner.UpdateAsync(subscription, cancellationToken);

        public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
            Inner.DeleteAsync(ownerId, id, cancellationToken);
    }
}
