namespace FlowTrim.Application.Insights;

/// <summary>Rol de un mensaje en la conversación con el modelo.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>Llamada a herramienta pedida por el modelo; los argumentos llegan como JSON sin validar.</summary>
public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// Mensaje de la conversación. <c>ToolCalls</c> solo en mensajes del asistente que pidieron herramientas;
/// <c>ToolCallId</c> solo en mensajes <see cref="ChatRole.Tool"/> que devuelven el resultado de una llamada.
/// </summary>
public sealed record ChatMessage(
    ChatRole Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<ChatToolCall>? ToolCalls = null)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);

    public static ChatMessage User(string content) => new(ChatRole.User, content);

    public static ChatMessage Assistant(IReadOnlyList<ChatToolCall> toolCalls) => new(ChatRole.Assistant, null, ToolCalls: toolCalls);

    public static ChatMessage ToolResult(string toolCallId, string content) => new(ChatRole.Tool, content, toolCallId);
}

/// <summary>Herramienta que el modelo puede pedir; <c>ParametersJsonSchema</c> es un JSON Schema de objeto.</summary>
public sealed record ChatTool(string Name, string Description, string ParametersJsonSchema);

/// <summary>Petición de completado: mensajes en orden y, opcionalmente, las herramientas permitidas.</summary>
public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ChatTool>? Tools = null);

/// <summary>Consumo de tokens informado por el proveedor (0 si no lo informa).</summary>
public sealed record ChatUsage(int InputTokens, int OutputTokens);

/// <summary>
/// Respuesta del modelo: texto del asistente o llamadas a herramientas (si hay ambas, mandan las llamadas).
/// </summary>
public sealed record ChatCompletion(
    string? Text,
    IReadOnlyList<ChatToolCall> ToolCalls,
    string FinishReason,
    ChatUsage Usage)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// Frontera con el proveedor de IA (HU-33). La aplicación nunca conoce el SDK ni el formato del proveedor:
/// solo esta interfaz. Las implementaciones lanzan únicamente <see cref="ChatModelException"/> (o la
/// cancelación del llamador).
/// </summary>
public interface IChatModel
{
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}

/// <summary>Tipos de fallo controlados del proveedor.</summary>
public enum ChatModelErrorKind
{
    Timeout,
    RateLimited,
    Unauthorized,
    Unavailable,
    InvalidResponse,
    InputTooLarge,
}

/// <summary>
/// Fallo controlado del proveedor. El mensaje es genérico: nunca contiene la clave, la pregunta ni la respuesta
/// en bruto del proveedor.
/// </summary>
public sealed class ChatModelException(ChatModelErrorKind kind, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception($"The AI provider failed: {kind}.", innerException)
{
    public ChatModelErrorKind Kind { get; } = kind;

    /// <summary>Espera sugerida por el proveedor cuando <see cref="Kind"/> es <see cref="ChatModelErrorKind.RateLimited"/>.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
