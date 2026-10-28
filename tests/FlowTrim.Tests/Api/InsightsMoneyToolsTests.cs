using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Application.Insights.Tools;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Goals;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Api;

/// <summary>EP-16 HU-80: herramientas de mes, presupuestos y metas, y su evidencia en Insights (proveedor simulado).</summary>
public sealed class InsightsMoneyToolsTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Route = "/api/insights/questions";
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private readonly FlowTrimApiFactory _factory;

    public InsightsMoneyToolsTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AskingAboutBudgets_ReturnsEvidenceEqualToTheBudgetsEndpoint()
    {
        // AC-1.
        var owner = Seed();
        var client = _factory.CreateClientFor(owner);

        var answer = await (await client.PostAsJsonAsync(Route, new { question = "How are my budgets?" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var budgets = await client.GetFromJsonAsync<JsonElement>("/api/budgets");

        Assert.Equal("answered", answer.GetProperty("status").GetString());
        Assert.Contains("get_budgets", answer.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        var food = Assert.Single(budgets.GetProperty("items").EnumerateArray());
        var figures = answer.GetProperty("figures").EnumerateArray().ToList();
        Assert.Equal(food.GetProperty("spent").GetDecimal(),
            figures.Single(f => f.GetProperty("kind").GetString() == "budget_spent").GetProperty("value").GetDecimal());
        Assert.Equal(food.GetProperty("remaining").GetDecimal(),
            figures.Single(f => f.GetProperty("kind").GetString() == "budget_remaining").GetProperty("value").GetDecimal());
        Assert.Contains("recorded_only", answer.GetProperty("assumptions").EnumerateArray().Select(a => a.GetProperty("key").GetString()));
    }

    [Theory]
    [InlineData("How are my savings goals?", "get_goals", "goal_saved")]
    [InlineData("How much did I spend this month?", "get_month_spending", "month_expense")]
    public async Task AskingAboutGoalsOrTheMonth_UsesTheNewTools(string question, string tool, string kind)
    {
        var client = _factory.CreateClientFor(Seed());

        var answer = await (await client.PostAsJsonAsync(Route, new { question })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains(tool, answer.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains(kind, answer.GetProperty("figures").EnumerateArray().Select(f => f.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task MonthSpendingTool_InvalidMonth_IsAnInvalidArgumentsResult()
    {
        var owner = Seed();
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
            new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", owner)], "Test")),
            };
        var registry = scope.ServiceProvider.GetRequiredService<FinancialToolRegistry>();

        var invalid = await registry.ExecuteAsync(new Application.Insights.ChatToolCall("c", "get_month_spending", """{"month":"October"}"""), default);
        var goals = await registry.ExecuteAsync(new Application.Insights.ChatToolCall("c", "get_goals", "{}"), default);

        Assert.Equal(ToolStatus.InvalidArguments, invalid.Status);
        Assert.Equal(ToolStatus.Ok, goals.Status);
        Assert.Equal(["goal_saved", "goal_remaining", "goal_required_per_month"], goals.Figures.Select(f => f.Kind));
        Assert.Equal(400000m, goals.Figures[2].Value);
    }

    // Comida 400.000 COP con 330.000 gastados en octubre; meta Trip de 3.000.000 al 2027-03-31 con 600.000 ahorrados.
    private string Seed()
    {
        var owner = $"auth0|money-tools-{Guid.NewGuid():N}";
        var bank = Account.Create(owner, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var food = Category.Create(owner, "Food", CategoryKind.Expense, "orange", "food", Clock);
        _factory.Accounts.Items.Add(bank);
        _factory.Categories.Items.Add(food);
        _factory.Accounts.Transactions.Add(Transaction.Create(
            owner, TransactionType.Expense, bank.Id, null, food.Id, 330000m, new DateOnly(2026, 10, 5), null, null, Clock));
        _factory.Budgets.Items.Add(Budget.Create(owner, food.Id, "COP", 400000m, Clock));
        var trip = SavingsGoal.Create(owner, "Trip", "COP", 3000000m, new DateOnly(2027, 3, 31), Clock);
        _factory.Goals.Goals.Add(trip);
        _factory.Goals.Contributions.Add(GoalContribution.Create(owner, trip.Id, 600000m, new DateOnly(2026, 10, 1), null, Clock));
        return owner;
    }
}
