using System.Text.Json;
using FlowTrim.Application.Insights;
using FlowTrim.Application.Insights.Tools;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Infrastructure.Insights;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Insights;

/// <summary>HU-35: orquestación con un modelo guionizado, las herramientas reales y repositorios en memoria.</summary>
public sealed class AnswerQuestionHandlerTests
{
    private const string OwnerA = "auth0|answer-a";
    private const string OwnerB = "auth0|answer-b";

    private static readonly DateTimeOffset FixedNow = new(2026, 10, 19, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly InMemorySubscriptionRepository _subscriptions = new();
    private readonly InMemoryUsageRecordRepository _usage = new();
    private readonly FixedTimeProvider _clock = new(FixedNow);

    [Fact]
    public async Task Question_WithSummary_ReturnsEvidenceFromTools()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerA, "Cloud", 10m, "USD");
        var model = new ScriptedChatModel(Call("get_spending_summary"), Text("You spend 38,900 COP and 10 USD per month."));

        var result = await HandleAsync(model, "How much do I spend per month?");

        var response = result.Value!;
        Assert.Equal(InsightsAnswerStatus.Answered, response.Status);
        Assert.Equal("You spend 38,900 COP and 10 USD per month.", response.Answer);
        Assert.Equal(["get_spending_summary"], response.ToolsUsed);
        Assert.Contains(response.Figures, f => f is { Kind: InsightFigureKinds.MonthlyEquivalent, Value: 38900m, Currency: "COP" });
        Assert.Contains(response.Figures, f => f is { Kind: InsightFigureKinds.AnnualProjection, Value: 120m, Currency: "USD" });
        Assert.Contains(new InsightAssumption("no_conversion", ToolAssumptions.NoConversion), response.Assumptions);
        Assert.Equal(["summary"], response.Sources);
        Assert.Equal(2, result.ModelCalls);

        // El modelo recibe el prompt de sistema, la pregunta, las herramientas y el resultado como JSON.
        var first = model.Requests[0];
        Assert.Equal(ChatRole.System, first.Messages[0].Role);
        Assert.Equal(AnswerQuestionHandler.SystemPrompt, first.Messages[0].Content);
        Assert.Equal("How much do I spend per month?", first.Messages[1].Content);
        Assert.Equal(5, first.Tools!.Count);
        var toolMessage = Assert.Single(model.Requests[1].Messages, m => m.Role == ChatRole.Tool);
        Assert.Equal("call-0", toolMessage.ToolCallId);
        Assert.StartsWith("""{"tool":"get_spending_summary","status":"ok",""", toolMessage.Content);
    }

    [Fact]
    public async Task Question_ModelAnswersWithoutTools_ReturnsUnsupported()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(Text("A joke: you spend 999,999 COP."));

        var response = (await HandleAsync(model, "Tell me a joke")).Value!;

        Assert.Equal(InsightsAnswerStatus.Unsupported, response.Status);
        Assert.Equal(AnswerQuestionHandler.UnsupportedAnswer, response.Answer);
        Assert.Empty(response.Figures);
        Assert.Empty(response.ToolsUsed);
        Assert.DoesNotContain("999,999", response.Answer);
    }

    [Fact]
    public async Task Question_UnknownToolOrInvalidArguments_ContinuesSafely()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(
            Calls(("delete_subscription", "{}"), ("get_upcoming_charges", """{"withinDays":90}""")),
            Text("Netflix costs 999,999 COP."));

        var response = (await HandleAsync(model, "Delete Netflix")).Value!;

        // Ninguna herramienta leyó datos: el texto del modelo (con una cifra inventada) se descarta.
        Assert.Equal(InsightsAnswerStatus.NoData, response.Status);
        Assert.Equal(AnswerQuestionHandler.NoEvidenceAnswer, response.Answer);
        Assert.Empty(response.Figures);
        Assert.Empty(response.Assumptions);
        Assert.Empty(response.ToolsUsed);
        var results = model.Requests[1].Messages.Where(m => m.Role == ChatRole.Tool).Select(m => Status(m.Content!)).ToList();
        Assert.Equal([ToolStatus.UnknownTool, ToolStatus.InvalidArguments], results);
    }

    [Fact]
    public async Task Question_ModelKeepsCallingTools_StopsAfterThreeCalls()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(Call("get_spending_summary"), Call("get_spending_analytics"), Text("Done."));

        var result = await HandleAsync(model, "Tell me everything");

        Assert.Equal(3, result.ModelCalls);
        Assert.Equal(3, model.Requests.Count);
        Assert.NotNull(model.Requests[0].Tools);
        Assert.NotNull(model.Requests[1].Tools);
        Assert.Null(model.Requests[2].Tools);
        Assert.Equal("Done.", result.Value!.Answer);
        Assert.Equal(["get_spending_summary", "get_spending_analytics"], result.Value.ToolsUsed);
    }

    [Fact]
    public async Task Question_LastCallStillAsksForTools_IgnoresThemAndAnswersWithEvidence()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(Call("get_spending_summary"), Call("get_spending_summary"), Call("get_spending_summary"));

        var result = await HandleAsync(model, "How much do I spend?");

        Assert.Equal(3, result.ModelCalls);
        Assert.Equal(AnswerQuestionHandler.EvidenceOnlyAnswer, result.Value!.Answer);
        Assert.Equal(InsightsAnswerStatus.Answered, result.Value.Status);
        Assert.Equal(["get_spending_summary"], result.Value.ToolsUsed);
    }

    [Fact]
    public async Task Question_TooManyCallsInOneRound_RejectsTheExtraCalls()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(
            Calls(("get_spending_summary", "{}"), ("get_spending_analytics", "{}"), ("get_upcoming_charges", "{}"), ("get_spending_summary", "{}")),
            Text("Here you go."));

        var response = (await HandleAsync(model, "Everything please")).Value!;

        var toolMessages = model.Requests[1].Messages.Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.Equal(4, toolMessages.Count);
        Assert.Equal(ToolStatus.InvalidArguments, Status(toolMessages[3].Content!));
        Assert.Contains("Too many tool calls", toolMessages[3].Content);
        Assert.Equal(["get_spending_summary", "get_spending_analytics", "get_upcoming_charges"], response.ToolsUsed);
    }

    [Fact]
    public async Task Question_NoSubscriptions_ReturnsNoData()
    {
        var model = new ScriptedChatModel(Call("get_spending_summary"), Text("You have no subscriptions yet."));

        var response = (await HandleAsync(model, "How much do I spend?")).Value!;

        Assert.Equal(InsightsAnswerStatus.NoData, response.Status);
        Assert.Equal("You have no subscriptions yet.", response.Answer);
        Assert.Empty(response.Figures);
        Assert.Equal(["get_spending_summary"], response.ToolsUsed);
    }

    [Fact]
    public async Task Question_OtherOwnerName_DoesNotLeakData()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        Seed(OwnerB, "Gym", 90000m, "USD");
        var model = new ScriptedChatModel(
            Calls(("simulate_cancellation", """{"subscriptionNames":["Gym"]}"""), ("get_cost_per_use", """{"subscriptionName":"Gym"}""")),
            Text("I could not find Gym."));

        var response = (await HandleAsync(model, "How much do I save if I cancel Gym?")).Value!;

        Assert.Equal(InsightsAnswerStatus.NoData, response.Status);
        Assert.Empty(response.Figures);
        var sent = string.Join("\n", model.Requests.SelectMany(r => r.Messages).Select(m => m.Content));
        Assert.DoesNotContain("90000", sent);
        Assert.DoesNotContain("USD", sent);
    }

    [Fact]
    public async Task Question_LongAnswer_IsCapped()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(Call("get_spending_summary"), Text(new string('a', 5000)));

        var response = (await HandleAsync(model, "Spending?")).Value!;

        Assert.Equal(AnswerQuestionHandler.MaxAnswerLength, response.Answer.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Question_Empty_ReturnsValidationErrorWithoutCallingTheModel(string? question)
    {
        var model = new ScriptedChatModel();

        var result = await HandleAsync(model, question);

        Assert.False(result.IsSuccess);
        Assert.Contains("question", result.Errors.Keys);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Question_TooLong_ReturnsValidationErrorButTrimsFirst()
    {
        var model = new ScriptedChatModel(Text("ok"));

        var tooLong = await HandleAsync(model, new string('a', AnswerQuestionHandler.MaxQuestionLength + 1));
        var paddedButValid = await HandleAsync(model, "  " + new string('a', AnswerQuestionHandler.MaxQuestionLength) + "  ");

        Assert.False(tooLong.IsSuccess);
        Assert.True(paddedButValid.IsSuccess);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task Question_ProviderError_Propagates()
    {
        var model = new ScriptedChatModel(_ => throw new ChatModelException(ChatModelErrorKind.Unavailable));

        await Assert.ThrowsAsync<ChatModelException>(() => HandleAsync(model, "Spending?"));
    }

    [Fact]
    public async Task Question_FakeProvider_RunsEndToEnd()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var handler = new AnswerQuestionHandler(new FakeChatModel(), Registry());

        var response = (await handler.HandleAsync(new InsightsQuestionRequest("¿Cuánto gasto al mes?"), default)).Value!;

        Assert.Equal(InsightsAnswerStatus.Answered, response.Status);
        Assert.Equal(["get_spending_summary"], response.ToolsUsed);
        Assert.StartsWith(FakeChatModel.ExplanationPrefix, response.Answer);
    }

    [Fact]
    public async Task Figures_PerTool_MatchToolResults()
    {
        var netflix = Seed(OwnerA, "Netflix", 38900m, "COP", category: "Streaming", next: Today.AddDays(2));
        Seed(OwnerA, "Spotify", 16900m, "COP", category: "Music", next: Today.AddDays(40));
        _usage.Items.Add(UsageRecord.Create(netflix.Id, new DateOnly(2026, 10, 10), 4, null));
        var registry = Registry();

        async Task<IReadOnlyList<InsightFigure>> FiguresOf(string tool, string arguments) =>
            (await registry.ExecuteAsync(new ChatToolCall("c", tool, arguments), default)).Figures;

        var summary = await FiguresOf("get_spending_summary", "{}");
        Assert.Equal(
            [(InsightFigureKinds.MonthlyEquivalent, 55800m), (InsightFigureKinds.AnnualProjection, 669600m)],
            summary.Select(f => (f.Kind, f.Value)));

        var analytics = await FiguresOf("get_spending_analytics", "{}");
        Assert.Equal(InsightFigureKinds.MonthlyTotal, analytics[0].Kind);
        Assert.Equal(55800m, analytics[0].Value);
        Assert.Contains(analytics, f => f is { Kind: InsightFigureKinds.CategorySpend, Subject: "Streaming", Value: 38900m });

        var upcoming = Assert.Single(await FiguresOf("get_upcoming_charges", "{}"));
        Assert.Equal(("Netflix", new DateOnly(2026, 10, 21), 38900m, "COP"), (upcoming.Subject, upcoming.Date, upcoming.Value, upcoming.Currency));

        var simulation = await FiguresOf("simulate_cancellation", """{"subscriptionNames":["Netflix"],"horizons":[12]}""");
        Assert.Equal(
            [(InsightFigureKinds.SelectedMonthly, (int?)null, 38900m), (InsightFigureKinds.Savings, 12, 466800m)],
            simulation.Select(f => (f.Kind, f.Months, f.Value)));
        Assert.All(simulation, f => Assert.Null(f.Subject));
        Assert.Empty(await FiguresOf("simulate_cancellation", """{"subscriptionNames":["Disney"]}"""));

        var costPerUse = await FiguresOf("get_cost_per_use", """{"subscriptionName":"Netflix","from":"2026-10-01","to":"2026-10-31"}""");
        Assert.Equal(
            [(InsightFigureKinds.PeriodCost, 38900m, "COP"), (InsightFigureKinds.DeclaredUses, 4m, null), (InsightFigureKinds.CostPerUse, 9725m, "COP")],
            costPerUse.Select(f => (f.Kind, f.Value, f.Currency)));
        var notCalculable = await FiguresOf("get_cost_per_use", """{"subscriptionName":"Spotify"}""");
        Assert.DoesNotContain(notCalculable, f => f.Kind == InsightFigureKinds.CostPerUse);
    }

    [Fact]
    public async Task Question_RejectedCallsThenValidOne_KeepsTheModelText()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var model = new ScriptedChatModel(
            Calls(("get_upcoming_charges", """{"withinDays":90}""")),
            Call("get_upcoming_charges"),
            Text("Netflix is due soon."));

        var response = (await HandleAsync(model, "What is due?")).Value!;

        Assert.Equal(InsightsAnswerStatus.Answered, response.Status);
        Assert.Equal("Netflix is due soon.", response.Answer);
        Assert.Equal(["get_upcoming_charges"], response.ToolsUsed);
    }

    [Fact]
    public async Task Question_LongAnswerEndingInEmoji_IsCappedWithoutSplittingIt()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");
        var text = new string('a', AnswerQuestionHandler.MaxAnswerLength - 1) + "\U0001F4B8 tail";
        var model = new ScriptedChatModel(Call("get_spending_summary"), Text(text));

        var answer = (await HandleAsync(model, "How much?")).Value!.Answer;

        Assert.Equal(AnswerQuestionHandler.MaxAnswerLength - 1, answer.Length);
        Assert.False(char.IsHighSurrogate(answer[^1]));
    }

    [Theory]
    [InlineData(ToolAssumptions.Expected, "expected_charges")]
    [InlineData(ToolAssumptions.ConstantPrices, "constant_prices")]
    [InlineData(ToolAssumptions.ActiveOnly, "active_only")]
    [InlineData(ToolAssumptions.NoConversion, "no_conversion")]
    [InlineData(ToolAssumptions.DeclaredUsage, "declared_usage")]
    [InlineData(ToolAssumptions.NothingCancelled, "nothing_cancelled")]
    [InlineData("Some future assumption.", ToolAssumptions.OtherKey)]
    public void AssumptionKeys_AreStable(string text, string expectedKey)
    {
        Assert.Equal(expectedKey, ToolAssumptions.KeyOf(text));
    }

    [Fact]
    public void AssumptionKeys_CoverEveryAssumption()
    {
        var texts = typeof(ToolAssumptions).GetFields()
            .Where(field => field is { IsLiteral: true } && field.Name != nameof(ToolAssumptions.OtherKey))
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.All(texts, text => Assert.NotEqual(ToolAssumptions.OtherKey, ToolAssumptions.KeyOf(text)));
    }

    [Fact]
    public async Task Figures_AreNotSentToTheModel()
    {
        Seed(OwnerA, "Netflix", 38900m, "COP");

        var result = await Registry().ExecuteAsync(new ChatToolCall("c", "get_spending_summary", "{}"), default);

        Assert.NotEmpty(result.Figures);
        Assert.DoesNotContain("figures", result.ToJson(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isOk", result.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    // ----- Helpers -----

    private FinancialToolRegistry Registry() => TestFinancialTools.Create(_subscriptions, _usage, _clock, OwnerA);

    private Task<InsightsAnswerResult> HandleAsync(IChatModel model, string? question) =>
        new AnswerQuestionHandler(model, Registry()).HandleAsync(new InsightsQuestionRequest(question), default);

    private static ChatCompletion Text(string text) => new(text, [], "stop", new ChatUsage(10, 5));

    private static ChatCompletion Call(string tool) => Calls((tool, "{}"));

    private static ChatCompletion Calls(params (string Tool, string Arguments)[] calls) =>
        new(null, calls.Select((call, index) => new ChatToolCall($"call-{index}", call.Tool, call.Arguments)).ToList(), "tool_calls", new ChatUsage(10, 5));

    private static string Status(string toolJson) => JsonDocument.Parse(toolJson).RootElement.GetProperty("status").GetString()!;

    private Subscription Seed(string owner, string name, decimal amount, string currency, string? category = null, DateOnly? next = null)
    {
        var subscription = Subscription.Create(owner, name, category, amount, currency, BillingCycle.Monthly, next ?? Today.AddDays(10), _clock);
        _subscriptions.Items.Add(subscription);
        return subscription;
    }
}

/// <summary>Modelo de prueba que devuelve respuestas en orden y guarda cada petición recibida.</summary>
public sealed class ScriptedChatModel : IChatModel
{
    private readonly Func<ChatRequest, ChatCompletion> _respond;

    public ScriptedChatModel(params ChatCompletion[] completions)
    {
        var queue = new Queue<ChatCompletion>(completions);
        _respond = _ => queue.Dequeue();
    }

    public ScriptedChatModel(Func<ChatRequest, ChatCompletion> respond) => _respond = respond;

    public List<ChatRequest> Requests { get; } = [];

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // Copia de los mensajes: el orquestador sigue agregando a la misma lista después de cada llamada.
        Requests.Add(request with { Messages = request.Messages.ToList() });
        return Task.FromResult(_respond(request));
    }
}
