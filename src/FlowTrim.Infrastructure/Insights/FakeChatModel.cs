using FlowTrim.Application.Insights;

namespace FlowTrim.Infrastructure.Insights;

/// <summary>
/// Proveedor simulado (por defecto): determinista y sin red. Si la conversación ya trae un resultado de
/// herramienta, lo explica citándolo tal cual; si no, pide la herramienta que encaje por palabras clave; si nada
/// encaja, responde que solo atiende preguntas sobre las suscripciones. Sirve para pruebas, CI y demos sin clave.
/// </summary>
public sealed class FakeChatModel : IChatModel
{
    public const string ExplanationPrefix = "Here is what FlowTrim calculated: ";
    public const string OutOfScopeAnswer =
        "I can only answer questions about your FlowTrim subscriptions, spending and upcoming charges.";

    // Palabra clave en la pregunta (inglés o español) → fragmento del nombre de la herramienta a pedir.
    private static readonly (string[] Keywords, string ToolFragment)[] Rules =
    [
        (["cancel", "save", "saving", "cancelar", "ahorr"], "simulat"),
        (["upcoming", "next charge", "due", "próxim", "proxim", "cobro", "vence"], "upcoming"),
        (["category", "categor", "most expensive", "más car", "mas car", "top"], "analytics"),
        (["per use", "por uso", "usage", "uso"], "cost"),
        (["spend", "total", "month", "year", "gasto", "mes", "año", "resumen", "summary"], "summary"),
    ];

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var noUsage = new ChatUsage(0, 0);

        var lastToolResult = request.Messages.LastOrDefault(m => m.Role == ChatRole.Tool);
        if (lastToolResult is not null)
        {
            return Task.FromResult(new ChatCompletion(ExplanationPrefix + lastToolResult.Content, [], "stop", noUsage));
        }

        var question = request.Messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content?.ToLowerInvariant() ?? string.Empty;
        var tool = FindTool(question, request.Tools ?? []);
        if (tool is null)
        {
            return Task.FromResult(new ChatCompletion(OutOfScopeAnswer, [], "stop", noUsage));
        }

        var call = new ChatToolCall("fake-call-1", tool.Name, "{}");
        return Task.FromResult(new ChatCompletion(null, [call], "tool_calls", noUsage));
    }

    private static ChatTool? FindTool(string question, IReadOnlyList<ChatTool> tools)
    {
        foreach (var (keywords, fragment) in Rules)
        {
            if (keywords.Any(question.Contains))
            {
                var match = tools.FirstOrDefault(t => t.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }
}
