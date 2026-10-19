namespace FlowTrim.Application.Insights.Tools;

/// <summary>
/// Herramienta financiera de solo lectura que el modelo puede pedir (HU-34). Envuelve handlers existentes: no
/// añade fórmulas. Se ejecuta en el ámbito de la petición, por lo que solo ve datos del propietario autenticado.
/// </summary>
public interface IFinancialTool
{
    string Name { get; }

    /// <summary>Nombre, descripción y JSON Schema de parámetros, en el formato de HU-33.</summary>
    ChatTool Definition { get; }

    Task<ToolResult> ExecuteAsync(string? argumentsJson, CancellationToken cancellationToken);
}

/// <summary>Base común: convierte los argumentos inválidos en un resultado, nunca en una excepción.</summary>
public abstract class FinancialTool : IFinancialTool
{
    public abstract string Name { get; }

    protected abstract string Description { get; }

    protected abstract string ParametersJsonSchema { get; }

    public ChatTool Definition => new(Name, Description, ParametersJsonSchema);

    public async Task<ToolResult> ExecuteAsync(string? argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            return await RunAsync(ToolArguments.Parse(argumentsJson), cancellationToken);
        }
        catch (ToolArgumentException exception)
        {
            return ToolResult.Invalid(Name, exception.Message);
        }
    }

    protected abstract Task<ToolResult> RunAsync(ToolArguments arguments, CancellationToken cancellationToken);

    protected ToolResult Ok(object data, IReadOnlyList<string> assumptions, params string[] sources) =>
        new(Name, ToolStatus.Ok, data, assumptions, sources);

    /// <summary>Compara nombres sin distinguir mayúsculas y sin espacios sobrantes ("Música" = " música ").</summary>
    protected static bool SameName(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lista cerrada de herramientas (allow-list). Un nombre desconocido devuelve <see cref="ToolStatus.UnknownTool"/>;
/// nunca hay búsqueda dinámica de tipos ni ejecución de algo fuera de esta lista.
/// </summary>
public sealed class FinancialToolRegistry
{
    private readonly Dictionary<string, IFinancialTool> _tools;

    public FinancialToolRegistry(IEnumerable<IFinancialTool> tools) =>
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    public IReadOnlyList<ChatTool> Definitions => _tools.Values.Select(tool => tool.Definition).ToList();

    public Task<ToolResult> ExecuteAsync(ChatToolCall call, CancellationToken cancellationToken) =>
        _tools.TryGetValue(call.Name, out var tool)
            ? tool.ExecuteAsync(call.ArgumentsJson, cancellationToken)
            : Task.FromResult(ToolResult.Unknown(call.Name));
}
