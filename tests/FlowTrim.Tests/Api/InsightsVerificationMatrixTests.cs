using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowTrim.Application.Insights;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;
using FlowTrim.Tests.Insights;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Api;

/// <summary>
/// HU-37: matriz de verificación. Cada pregunta representativa recorre la API con un modelo guionizado que pide una
/// herramienta y luego escribe un número falso; las cifras de la respuesta deben ser idénticas a las del endpoint
/// determinista del mismo propietario. También fija el contrato de la respuesta y lo que nunca recibe el modelo.
/// </summary>
public sealed class InsightsVerificationMatrixTests : IClassFixture<FlowTrimApiFactory>, IDisposable
{
    private const string Route = "/api/insights/questions";
    private const string WrongAnswer = "You pay exactly 1 COP for everything.";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly FlowTrimApiFactory _factory;
    private readonly List<IDisposable> _hosts = [];

    public InsightsVerificationMatrixTests(FlowTrimApiFactory factory) => _factory = factory;

    public void Dispose() => _hosts.ForEach(host => host.Dispose());

    [Fact]
    public async Task Q1_MonthlySpending_FiguresEqualSummary()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP");
        Seed(owner, "Cloud", 10m, "USD", BillingCycle.Yearly);

        var (body, client) = await AskAsync(owner, "get_spending_summary", "{}");

        var summary = await client.GetFromJsonAsync<JsonElement>("/api/summary");
        var expected = summary.GetProperty("currencies").EnumerateArray()
            .SelectMany(c => new[]
            {
                ("monthly_equivalent", c.GetProperty("currency").GetString(), c.GetProperty("monthlyEquivalent").GetDecimal()),
                ("annual_projection", c.GetProperty("currency").GetString(), c.GetProperty("annualProjection").GetDecimal()),
            });
        Assert.Equal(expected.Order(), Figures(body).Select(f => (f.Kind, f.Currency, f.Value)).Order());
    }

    [Fact]
    public async Task Q2_ChargesInSevenDays_FiguresEqualUpcoming()
    {
        var owner = NewOwner();
        Seed(owner, "Overdue gym", 90000m, "COP", next: Today.AddDays(-2));
        Seed(owner, "Netflix", 38900m, "COP", next: Today.AddDays(3));
        Seed(owner, "Later", 5m, "USD", next: Today.AddDays(20));

        var (body, client) = await AskAsync(owner, "get_upcoming_charges", """{"withinDays":7}""");

        var upcoming = await client.GetFromJsonAsync<JsonElement>("/api/subscriptions/upcoming");
        var expected = upcoming.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("daysUntilCharge").GetInt32() <= 7)
            .Select(i => (i.GetProperty("name").GetString(), i.GetProperty("nextBillingDate").GetString(),
                i.GetProperty("amount").GetDecimal(), i.GetProperty("currency").GetString()))
            .ToList();
        var actual = Figures(body).Select(f => (f.Subject, f.Date, f.Value, f.Currency)).ToList();
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Q3_SavingsIfCancelled_FiguresEqualSimulation()
    {
        var owner = NewOwner();
        var netflix = Seed(owner, "Netflix", 38900m, "COP");
        Seed(owner, "Spotify", 16900m, "COP");

        var (body, client) = await AskAsync(owner, "simulate_cancellation", """{"subscriptionNames":["netflix"],"horizons":[12]}""");

        var response = await client.PostAsJsonAsync("/api/simulations", new { subscriptionIds = new[] { netflix.Id }, horizons = new[] { 12 } });
        var simulation = await response.Content.ReadFromJsonAsync<JsonElement>();
        var currency = Assert.Single(simulation.GetProperty("currencies").EnumerateArray());
        var saving = Assert.Single(currency.GetProperty("savings").EnumerateArray());
        Assert.Equal(
            [
                ("selected_monthly", (int?)null, currency.GetProperty("selectedMonthly").GetDecimal()),
                ("savings", saving.GetProperty("months").GetInt32(), saving.GetProperty("amount").GetDecimal()),
            ],
            Figures(body).Select(f => (f.Kind, f.Months, f.Value)));
    }

    [Fact]
    public async Task Q4_CostPerUse_FiguresEqualCostPerUseEndpoint()
    {
        var owner = NewOwner();
        var netflix = Seed(owner, "Netflix", 38900m, "COP");
        _factory.UsageRecords.Items.Add(UsageRecord.Create(netflix.Id, Today.AddDays(-1), 4, null));
        var (from, to) = (Today.AddDays(-10), Today);

        var (body, client) = await AskAsync(owner, "get_cost_per_use",
            $$"""{"subscriptionName":"Netflix","from":"{{from:yyyy-MM-dd}}","to":"{{to:yyyy-MM-dd}}"}""");

        var costPerUse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/subscriptions/{netflix.Id}/cost-per-use?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.True(costPerUse.GetProperty("calculable").GetBoolean());
        Assert.Equal(
            [
                ("period_cost", costPerUse.GetProperty("totalPaid").GetDecimal()),
                ("declared_uses", costPerUse.GetProperty("totalUses").GetDecimal()),
                ("cost_per_use", costPerUse.GetProperty("costPerUse").GetDecimal()),
            ],
            Figures(body).Select(f => (f.Kind, f.Value)));
    }

    [Fact]
    public async Task Question_ModelTextContradictsFigures_FiguresStayDeterministic()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP");

        var (body, _) = await AskAsync(owner, "get_spending_summary", "{}");

        // El texto falso llega tal cual como explicación, pero ninguna cifra sale de él.
        Assert.Equal(WrongAnswer, body.GetProperty("answer").GetString());
        Assert.DoesNotContain(Figures(body), f => f.Value == 1m);
        Assert.Contains(Figures(body), f => f is { Kind: "monthly_equivalent", Value: 38900m });
    }

    [Fact]
    public async Task Question_ModelRequests_ContainNoIdsOwnerOrToken()
    {
        const string token = "token-marker-7f3a";
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP", next: Today.AddDays(2));
        Seed(owner, "Spotify", 16900m, "COP");
        var model = new ScriptedChatModel(
            Calls(("get_upcoming_charges", "{}"), ("get_spending_analytics", "{}"), ("simulate_cancellation", """{"subscriptionNames":["Netflix"]}""")),
            Calls(("get_cost_per_use", """{"subscriptionName":"Netflix"}"""), ("get_spending_summary", "{}")),
            new ChatCompletion(WrongAnswer, [], "stop", new ChatUsage(1, 1)));
        var client = WithModel(model).CreateClientFor(owner);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.PostAsJsonAsync(Route, new { question = "Tell me everything about my charges" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = string.Join('\n', model.Requests.SelectMany(r => r.Messages)
            .SelectMany(m => new[] { m.Content ?? string.Empty }
                .Concat(m.ToolCalls?.Select(c => c.ArgumentsJson) ?? [])));
        Assert.Equal(5, model.Requests[^1].Messages.Count(m => m.Role == ChatRole.Tool));
        Assert.DoesNotMatch(GuidPattern(), sent);
        Assert.DoesNotContain(owner, sent);
        Assert.DoesNotContain(token, sent);
    }

    [Fact]
    public async Task Question_Answered_ResponseShapeMatchesContract()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP", next: Today.AddDays(2));

        var (body, _) = await AskAsync(owner, "get_upcoming_charges", "{}");

        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(["answer", "assumptions", "figures", "sources", "status", "toolsUsed"], PropertyNames(body));
        Assert.All(body.GetProperty("figures").EnumerateArray(), figure =>
            Assert.Subset(new HashSet<string> { "kind", "label", "subject", "value", "currency", "months", "date" }, PropertyNames(figure).ToHashSet()));
        Assert.All(body.GetProperty("assumptions").EnumerateArray(), assumption =>
            Assert.Equal(["key", "text"], PropertyNames(assumption)));
        Assert.DoesNotContain("\"id\"", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(GuidPattern(), body.GetRawText());
    }

    // ----- Helpers -----

    private async Task<(JsonElement Body, HttpClient Client)> AskAsync(string owner, string tool, string arguments)
    {
        var model = new ScriptedChatModel(
            Calls((tool, arguments)),
            new ChatCompletion(WrongAnswer, [], "stop", new ChatUsage(1, 1)));
        var client = WithModel(model).CreateClientFor(owner);

        var response = await client.PostAsJsonAsync(Route, new { question = "Representative question" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal([tool], body.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        return (body, client);
    }

    private WebApplicationFactory<Program> WithModel(IChatModel model)
    {
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(model)));
        _hosts.Add(host);
        return host;
    }

    private Subscription Seed(string owner, string name, decimal amount, string currency,
        BillingCycle cycle = BillingCycle.Monthly, DateOnly? next = null)
    {
        var subscription = Subscription.Create(owner, name, null, amount, currency, cycle,
            next ?? Today.AddDays(15), new FixedTimeProvider(DateTimeOffset.UtcNow.AddDays(-30)));
        _factory.Subscriptions.Items.Add(subscription);
        return subscription;
    }

    private static ChatCompletion Calls(params (string Tool, string Arguments)[] calls) =>
        new(null, calls.Select((call, index) => new ChatToolCall($"call-{index}", call.Tool, call.Arguments)).ToList(),
            "tool_calls", new ChatUsage(1, 1));

    private static IReadOnlyList<(string Kind, string? Subject, decimal Value, string? Currency, int? Months, string? Date)> Figures(JsonElement body) =>
        body.GetProperty("figures").EnumerateArray().Select(f => (
            f.GetProperty("kind").GetString()!,
            f.TryGetProperty("subject", out var subject) ? subject.GetString() : null,
            f.GetProperty("value").GetDecimal(),
            f.TryGetProperty("currency", out var currency) ? currency.GetString() : null,
            f.TryGetProperty("months", out var months) ? months.GetInt32() : (int?)null,
            f.TryGetProperty("date", out var date) ? date.GetString() : null)).ToList();

    private static List<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();

    private static string NewOwner() => $"auth0|matrix-{Guid.NewGuid():N}";

    private static Regex GuidPattern() =>
        new("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
}
