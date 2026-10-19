using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowTrim.Application.Insights;
using FlowTrim.Infrastructure.Insights;
using FlowTrim.Tests.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowTrim.Tests.Insights;

/// <summary>Adaptador compatible con OpenAI (HU-33) probado sin red, con respuestas grabadas.</summary>
public sealed class OpenAiCompatibleChatModelTests
{
    private const string FakeKey = "test-key-not-real-123";
    private const string Question = "How much do I spend on streaming?";

    private static readonly ChatRequest SimpleRequest = new([ChatMessage.System("You explain FlowTrim data."), ChatMessage.User(Question)]);

    private const string TextResponse = """
        {"choices":[{"message":{"role":"assistant","content":"You spend 38,900 COP per month."},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":42,"completion_tokens":9}}
        """;

    private const string ToolCallResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
           {"id":"call_1","type":"function","function":{"name":"get_summary","arguments":"{}"}},
           {"id":"call_2","type":"function","function":{"name":"simulate_cancellation","arguments":"{\"months\":12}"}}]},
          "finish_reason":"tool_calls"}],"usage":{"prompt_tokens":60,"completion_tokens":20}}
        """;

    [Fact]
    public async Task TextResponse_MapsTextFinishReasonAndUsage()
    {
        var (model, _, _) = Build(_ => Json(HttpStatusCode.OK, TextResponse));

        var completion = await model.CompleteAsync(SimpleRequest, CancellationToken.None);

        Assert.Equal("You spend 38,900 COP per month.", completion.Text);
        Assert.False(completion.HasToolCalls);
        Assert.Equal("stop", completion.FinishReason);
        Assert.Equal(new ChatUsage(42, 9), completion.Usage);
    }

    [Fact]
    public async Task ToolCallsResponse_MapsCallsWithRawArguments()
    {
        var (model, _, _) = Build(_ => Json(HttpStatusCode.OK, ToolCallResponse));

        var completion = await model.CompleteAsync(SimpleRequest, CancellationToken.None);

        Assert.Null(completion.Text);
        Assert.Equal(["get_summary", "simulate_cancellation"], completion.ToolCalls.Select(c => c.Name));
        Assert.Equal("call_2", completion.ToolCalls[1].Id);
        Assert.Equal("{\"months\":12}", completion.ToolCalls[1].ArgumentsJson);
        Assert.Equal("tool_calls", completion.FinishReason);
    }

    [Fact]
    public async Task Request_SendsBearerModelMessagesToolsAndMaxTokens()
    {
        var (model, handler, _) = Build(_ => Json(HttpStatusCode.OK, TextResponse));
        var request = new ChatRequest(
            [ChatMessage.User(Question), ChatMessage.Assistant([new ChatToolCall("call_1", "get_summary", "{}")]), ChatMessage.ToolResult("call_1", "{\"monthly\":38900}")],
            [new ChatTool("get_summary", "Monthly totals per currency.", """{"type":"object","properties":{}}""")]);

        await model.CompleteAsync(request, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://models.example.test/inference/chat/completions", sent.Uri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", FakeKey).ToString(), sent.Authorization);
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal("openai/gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(400, root.GetProperty("max_tokens").GetInt32());
        var messages = root.GetProperty("messages");
        Assert.Equal(["user", "assistant", "tool"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("call_1", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
        var tool = root.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ChatModelErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ChatModelErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, ChatModelErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ChatModelErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ChatModelErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, ChatModelErrorKind.Unavailable)]
    public async Task ErrorStatus_MapsToKind(HttpStatusCode status, ChatModelErrorKind expected)
    {
        var (model, _, _) = Build(_ => Json(status, """{"error":{"message":"provider detail"}}"""));

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest, CancellationToken.None));

        Assert.Equal(expected, error.Kind);
        Assert.DoesNotContain("provider detail", error.Message);
    }

    [Fact]
    public async Task RateLimited_CapturesRetryAfter()
    {
        var (model, _, _) = Build(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":"   "},"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"message":{"content":123}}]}""")]
    public async Task UnusableBody_IsInvalidResponse(string body)
    {
        var (model, _, _) = Build(_ => Json(HttpStatusCode.OK, body));

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest, CancellationToken.None));

        Assert.Equal(ChatModelErrorKind.InvalidResponse, error.Kind);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnavailable()
    {
        var (model, _, _) = Build(_ => throw new HttpRequestException("Name resolution failed for models.example.test"));

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest, CancellationToken.None));

        Assert.Equal(ChatModelErrorKind.Unavailable, error.Kind);
        Assert.DoesNotContain("models.example.test", error.Message);
    }

    [Fact]
    public async Task SlowProvider_TimesOut()
    {
        var (model, _, _) = Build(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Json(HttpStatusCode.OK, TextResponse);
        }, timeoutSeconds: 1);

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(SimpleRequest, CancellationToken.None));

        Assert.Equal(ChatModelErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var (model, _, _) = Build(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Json(HttpStatusCode.OK, TextResponse);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.CompleteAsync(SimpleRequest, cancellation.Token));
    }

    [Fact]
    public async Task InputTooLarge_SendsNothing()
    {
        var (model, handler, _) = Build(_ => Json(HttpStatusCode.OK, TextResponse));
        var request = new ChatRequest([ChatMessage.User(new string('x', 12_001))]);

        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.CompleteAsync(request, CancellationToken.None));

        Assert.Equal(ChatModelErrorKind.InputTooLarge, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Logs_NeverContainKeyQuestionOrAnswer()
    {
        var (model, _, logs) = Build(_ => Json(HttpStatusCode.OK, TextResponse));
        await model.CompleteAsync(SimpleRequest, CancellationToken.None);
        var (failing, _, failingLogs) = Build(_ => Json(HttpStatusCode.Unauthorized, """{"error":"bad key"}"""));
        await Assert.ThrowsAsync<ChatModelException>(() => failing.CompleteAsync(SimpleRequest, CancellationToken.None));

        var text = logs.AllText() + failingLogs.AllText();
        Assert.Contains("Chat completion via OpenAiCompatible model openai/gpt-4o-mini: Success", text);
        Assert.Contains("Unauthorized", text);
        Assert.DoesNotContain(FakeKey, text);
        Assert.DoesNotContain(Question, text);
        Assert.DoesNotContain("38,900", text);
        Assert.DoesNotContain("bad key", text);
    }

    private static (OpenAiCompatibleChatModel Model, StubHandler Handler, LogCapture Logs) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond, int timeoutSeconds = 20) =>
        Build((request, _) => Task.FromResult(respond(request)), timeoutSeconds);

    private static (OpenAiCompatibleChatModel Model, StubHandler Handler, LogCapture Logs) Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 20)
    {
        var handler = new StubHandler(respond);
        var logs = new LogCapture();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var options = Options.Create(new ChatModelOptions
        {
            Kind = ChatModelOptions.OpenAiCompatibleKind,
            BaseUrl = "https://models.example.test/inference/",
            Model = "openai/gpt-4o-mini",
            ApiKey = FakeKey,
            TimeoutSeconds = timeoutSeconds,
        });
        var model = new OpenAiCompatibleChatModel(new HttpClient(handler), options, loggerFactory.CreateLogger<OpenAiCompatibleChatModel>());
        return (model, handler, logs);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Handler HTTP falso: responde lo indicado y guarda cada petición (URL, cabecera y cuerpo).</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return await respond(request, cancellationToken);
        }
    }
}
