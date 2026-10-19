using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowTrim.Application.Insights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowTrim.Infrastructure.Insights;

/// <summary>
/// Adaptador para cualquier endpoint <c>/chat/completions</c> con formato OpenAI (GitHub Models, OpenRouter,
/// Groq, Gemini en modo compatible...). Cambiar de proveedor es cambiar configuración, no código. Traduce todo fallo
/// a <see cref="ChatModelException"/> y nunca registra la clave, las preguntas ni las respuestas.
/// </summary>
public sealed class OpenAiCompatibleChatModel(
    HttpClient httpClient,
    IOptions<ChatModelOptions> options,
    ILogger<OpenAiCompatibleChatModel> logger) : IChatModel
{
    private readonly ChatModelOptions _options = options.Value;

    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // El tamaño se controla antes de llamar: una pregunta enorme nunca sale de la API.
        var inputCharacters = request.Messages.Sum(m => m.Content?.Length ?? 0);
        if (inputCharacters > _options.MaxInputCharacters)
        {
            Log(LogLevel.Warning, ChatModelErrorKind.InputTooLarge.ToString(), 0, null, null);
            throw new ChatModelException(ChatModelErrorKind.InputTooLarge);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint())
            {
                Content = new StringContent(BuildBody(request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var failure = MapStatus(response);
                Log(LogLevel.Warning, failure.Kind.ToString(), elapsed, (int)response.StatusCode, null);
                throw failure;
            }

            var completion = Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            Log(LogLevel.Information, "Success", elapsed, (int)response.StatusCode, completion.Usage);
            return completion;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Se canceló por nuestro límite de tiempo, no por el llamador.
            Log(LogLevel.Warning, ChatModelErrorKind.Timeout.ToString(), Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, null, null);
            throw new ChatModelException(ChatModelErrorKind.Timeout);
        }
        catch (HttpRequestException exception)
        {
            // DNS, conexión rechazada, TLS...: el proveedor no está disponible. No se adjunta el detalle al mensaje.
            Log(LogLevel.Warning, ChatModelErrorKind.Unavailable.ToString(), Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, null, null);
            throw new ChatModelException(ChatModelErrorKind.Unavailable, innerException: exception);
        }
    }

    private Uri Endpoint() => new($"{_options.BaseUrl.TrimEnd('/')}/chat/completions");

    private JsonObject BuildBody(ChatRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxOutputTokens,
            ["messages"] = new JsonArray(request.Messages.Select(ToJson).ToArray<JsonNode?>()),
        };

        if (request.Tools is { Count: > 0 } tools)
        {
            body["tools"] = new JsonArray(tools.Select(tool => (JsonNode?)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema),
                },
            }).ToArray());
        }

        return body;
    }

    private static JsonObject ToJson(ChatMessage message)
    {
        var json = new JsonObject
        {
            ["role"] = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "tool",
                _ => throw new InvalidOperationException($"Unsupported chat role: {message.Role}"),
            },
            ["content"] = message.Content,
        };

        if (message.ToolCallId is not null)
        {
            json["tool_call_id"] = message.ToolCallId;
        }

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            json["tool_calls"] = new JsonArray(calls.Select(call => (JsonNode?)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson },
            }).ToArray());
        }

        return json;
    }

    private static ChatModelException MapStatus(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ChatModelException(ChatModelErrorKind.Unauthorized),
        HttpStatusCode.TooManyRequests => new ChatModelException(ChatModelErrorKind.RateLimited, RetryAfter(response)),
        // 5xx y cualquier otro estado inesperado (p. ej. un modelo mal configurado): el proveedor no sirve la petición.
        _ => new ChatModelException(ChatModelErrorKind.Unavailable),
    };

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static ChatCompletion Parse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var choice = root?["choices"]?.AsArray().FirstOrDefault()
                ?? throw new ChatModelException(ChatModelErrorKind.InvalidResponse);
            var message = choice["message"] ?? throw new ChatModelException(ChatModelErrorKind.InvalidResponse);

            var calls = message["tool_calls"]?.AsArray()
                .Select(call => new ChatToolCall(
                    call?["id"]?.GetValue<string>() ?? string.Empty,
                    call?["function"]?["name"]?.GetValue<string>() ?? string.Empty,
                    call?["function"]?["arguments"]?.GetValue<string>() ?? "{}"))
                .Where(call => call.Name.Length > 0)
                .ToList() ?? [];
            var text = message["content"]?.GetValue<string>();

            if (calls.Count == 0 && string.IsNullOrWhiteSpace(text))
            {
                throw new ChatModelException(ChatModelErrorKind.InvalidResponse);
            }

            var usage = root?["usage"];
            return new ChatCompletion(
                calls.Count > 0 ? null : text,
                calls,
                choice["finish_reason"]?.GetValue<string>() ?? "unknown",
                new ChatUsage(usage?["prompt_tokens"]?.GetValue<int>() ?? 0, usage?["completion_tokens"]?.GetValue<int>() ?? 0));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new ChatModelException(ChatModelErrorKind.InvalidResponse, innerException: exception);
        }
    }

    // Una línea por llamada, sin contenido: proveedor, modelo, resultado, duración, estado HTTP y tokens.
    private void Log(LogLevel level, string outcome, double elapsedMs, int? statusCode, ChatUsage? usage) =>
        logger.Log(level,
            "Chat completion via {Provider} model {Model}: {Outcome} in {ElapsedMs} ms (HTTP {StatusCode}, tokens in {InputTokens}, out {OutputTokens})",
            ChatModelOptions.OpenAiCompatibleKind, _options.Model, outcome, Math.Round(elapsedMs, 1),
            statusCode, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0);
}
