using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Api.Insights;
using FlowTrim.Application.Insights;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;
using FlowTrim.Tests.Insights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowTrim.Tests.Api;

/// <summary>HU-35 a través de HTTP: proveedor simulado por defecto, aislamiento, límites y errores seguros.</summary>
public sealed class InsightsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Route = "/api/insights/questions";

    private readonly FlowTrimApiFactory _factory;

    public InsightsApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Subscriptions.Items.Clear();
    }

    [Fact]
    public async Task Question_WithSummary_ReturnsEvidence()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP");
        Seed(owner, "Cloud", 10m, "USD");
        var client = _factory.CreateClientFor(owner);

        var response = await client.PostAsJsonAsync(Route, new { question = "How much do I spend per month?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(["get_spending_summary"], body.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("summary", body.GetProperty("sources").EnumerateArray().Select(s => s.GetString()));
        Assert.NotEmpty(body.GetProperty("assumptions").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("answer").GetString()));

        // Las cifras coinciden con GET /api/summary del mismo propietario.
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/summary");
        var figures = body.GetProperty("figures").EnumerateArray()
            .Where(f => f.GetProperty("kind").GetString() == "monthly_equivalent")
            .ToDictionary(f => f.GetProperty("currency").GetString()!, f => f.GetProperty("value").GetDecimal());
        var expected = summary.GetProperty("currencies").EnumerateArray()
            .ToDictionary(c => c.GetProperty("currency").GetString()!, c => c.GetProperty("monthlyEquivalent").GetDecimal());
        Assert.Equal(expected, figures);
        var figure = body.GetProperty("figures")[0];
        Assert.False(figure.TryGetProperty("subject", out _));
        Assert.Equal("Monthly equivalent", figure.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Question_Unsupported_ReturnsFixedMessage()
    {
        var response = await _factory.CreateClientFor(NewOwner()).PostAsJsonAsync(Route, new { question = "Tell me a joke" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unsupported", body.GetProperty("status").GetString());
        Assert.Equal(AnswerQuestionHandler.UnsupportedAnswer, body.GetProperty("answer").GetString());
        Assert.Equal(0, body.GetProperty("figures").GetArrayLength());
    }

    [Fact]
    public async Task Question_TooLong_ReturnsValidationProblem()
    {
        var model = new ScriptedChatModel();
        using var factory = WithModel(model);

        var response = await ClientFor(factory, NewOwner()).PostAsJsonAsync(Route, new { question = new string('a', 501) });

        await AssertValidationProblemAsync(response);
        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData("""{"question":""}""")]
    [InlineData("""{"question":"   "}""")]
    [InlineData("""{}""")]
    public async Task Question_Empty_ReturnsValidationProblem(string json)
    {
        var response = await _factory.CreateClientFor(NewOwner()).PostAsync(
            Route, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        await AssertValidationProblemAsync(response);
    }

    [Fact]
    public async Task Question_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Route, new { question = "How much do I spend?" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Question_CannotAccessOtherOwner()
    {
        var ownerA = NewOwner();
        var ownerB = NewOwner();
        Seed(ownerA, "Netflix", 38900m, "COP");
        Seed(ownerB, "Gym", 90000m, "USD");
        var model = new ScriptedChatModel(
            new ChatCompletion(null, [new ChatToolCall("c1", "simulate_cancellation", """{"subscriptionNames":["Gym"]}""")], "tool_calls", new ChatUsage(1, 1)),
            new ChatCompletion("Not found.", [], "stop", new ChatUsage(1, 1)));
        using var factory = WithModel(model);

        var response = await ClientFor(factory, ownerA).PostAsJsonAsync(Route, new { question = "Cancel Gym?" });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"no_data\"", text);
        Assert.DoesNotContain("90000", text);
        Assert.DoesNotContain("USD", text);
        var sentToModel = string.Join("\n", model.Requests.SelectMany(r => r.Messages).Select(m => m.Content));
        Assert.DoesNotContain("90000", sentToModel);
    }

    [Theory]
    [InlineData(ChatModelErrorKind.Timeout, HttpStatusCode.GatewayTimeout, InsightsEndpoints.TimeoutCode)]
    [InlineData(ChatModelErrorKind.RateLimited, HttpStatusCode.TooManyRequests, InsightsEndpoints.RateLimitedCode)]
    [InlineData(ChatModelErrorKind.Unavailable, HttpStatusCode.BadGateway, InsightsEndpoints.UnavailableCode)]
    [InlineData(ChatModelErrorKind.Unauthorized, HttpStatusCode.BadGateway, InsightsEndpoints.UnavailableCode)]
    [InlineData(ChatModelErrorKind.InvalidResponse, HttpStatusCode.BadGateway, InsightsEndpoints.UnavailableCode)]
    [InlineData(ChatModelErrorKind.InputTooLarge, HttpStatusCode.BadGateway, InsightsEndpoints.UnavailableCode)]
    public async Task ProviderFailure_ReturnsSafeGatewayError(ChatModelErrorKind kind, HttpStatusCode expectedStatus, string expectedCode)
    {
        var secret = "sk-provider-secret-detail";
        using var factory = WithModel(new ScriptedChatModel(_ =>
            throw new ChatModelException(kind, TimeSpan.FromSeconds(7.2), new HttpRequestException(secret))));

        var response = await ClientFor(factory, NewOwner()).PostAsJsonAsync(Route, new { question = "How much do I spend?" });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain(kind.ToString(), text);
        if (kind == ChatModelErrorKind.RateLimited)
        {
            Assert.Equal("8", Assert.Single(response.Headers.GetValues("Retry-After")));
        }
    }

    [Fact]
    public async Task Question_OverTimeBudget_Returns504()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Insights:RequestTimeoutSeconds", "5");
            builder.ConfigureTestServices(services => services.AddSingleton<IChatModel>(new HangingChatModel()));
        });

        var response = await ClientFor(factory, NewOwner()).PostAsJsonAsync(Route, new { question = "How much do I spend?" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InsightsEndpoints.TimeoutCode, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Question_CancelledByClient_IsNotLoggedAsServerError()
    {
        var logs = new LogCapture();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services => services.AddSingleton<IChatModel>(new HangingChatModel()));
        });
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ClientFor(factory, NewOwner()).PostAsJsonAsync(Route, new { question = "How much do I spend?" }, abort.Token));

        // El servidor termina la petición después de que el cliente se va: se espera su línea de log. El middleware de
        // excepciones de ASP.NET Core responde 499 sin pasar por GlobalExceptionHandler (no es un 500).
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!logs.Entries.Any(e => e.Message.Contains("responded 499")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Contains(logs.Entries, e => e.Message.Contains("responded 499"));
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Question_OverRateLimit_Returns429ForThatOwnerOnly()
    {
        var limited = _factory.CreateClientFor(NewOwner());
        for (var index = 0; index < 5; index++)
        {
            var allowed = await limited.PostAsJsonAsync(Route, new { question = "How much do I spend?" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var rejected = await limited.PostAsJsonAsync(Route, new { question = "How much do I spend?" });
        var other = await _factory.CreateClientFor(NewOwner()).PostAsJsonAsync(Route, new { question = "How much do I spend?" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(Assert.Single(rejected.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InsightsEndpoints.RateLimitedCode, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Question_Logs_DoNotContainQuestionOrAnswer()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", 38900m, "COP");
        var logs = new LogCapture();
        const string question = "How much do I spend per month on my private-marker subscriptions?";
        const string answer = "answer-marker: you spend 38,900 COP.";
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
            builder.ConfigureTestServices(services => services.AddSingleton<IChatModel>(new ScriptedChatModel(
                new ChatCompletion(null, [new ChatToolCall("c1", "get_spending_summary", "{}")], "tool_calls", new ChatUsage(120, 8)),
                new ChatCompletion(answer, [], "stop", new ChatUsage(300, 20)))));
        });

        var response = await ClientFor(factory, owner).PostAsJsonAsync(Route, new { question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var all = logs.AllText();
        Assert.DoesNotContain("private-marker", all);
        Assert.DoesNotContain("answer-marker", all);
        Assert.DoesNotContain("38900", all);
        var line = Assert.Single(logs.Entries, e => e.Message.StartsWith("Insights question", StringComparison.Ordinal));
        Assert.Contains("answered", line.Message);
        Assert.Contains("get_spending_summary", line.Message);
        Assert.Contains("420/28", line.Message);
    }

    [Fact]
    public async Task InvalidInsightsOptions_FailStartup()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Insights:RateLimit:PermitLimit", "0"));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void InsightsOptionsValidator_NamesTheSettingWithoutTheValue()
    {
        var result = new InsightsOptionsValidator().Validate(null, new InsightsOptions
        {
            RequestTimeoutSeconds = 999,
            RateLimit = new InsightsRateLimitOptions { PermitLimit = 0, WindowSeconds = 5 },
        });

        Assert.True(result.Failed);
        Assert.Equal(
            [
                "Insights:RequestTimeoutSeconds must be between 5 and 120.",
                "Insights:RateLimit:PermitLimit must be between 1 and 100.",
                "Insights:RateLimit:WindowSeconds must be between 10 and 3600.",
            ],
            result.Failures);
        Assert.True(new InsightsOptionsValidator().Validate(null, new InsightsOptions()).Succeeded);
    }

    // ----- Helpers -----

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string owner)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, owner);
        return client;
    }

    private static string NewOwner() => $"auth0|insights-{Guid.NewGuid():N}";

    private WebApplicationFactory<Program> WithModel(IChatModel model) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(model)));

    private void Seed(string owner, string name, decimal amount, string currency) =>
        _factory.Subscriptions.Items.Add(Subscription.Create(
            owner, name, null, amount, currency, BillingCycle.Monthly,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), new FixedTimeProvider(DateTimeOffset.UtcNow)));

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("question", out _));
    }

    /// <summary>Modelo que nunca responde hasta que se cancela: simula un proveedor colgado.</summary>
    private sealed class HangingChatModel : IChatModel
    {
        public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
