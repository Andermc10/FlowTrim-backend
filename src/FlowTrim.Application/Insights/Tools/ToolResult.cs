using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Estados posibles del resultado de una herramienta (valores estables que lee el modelo).</summary>
public static class ToolStatus
{
    public const string Ok = "ok";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string NotCalculable = "not_calculable";
    public const string InvalidArguments = "invalid_arguments";
    public const string UnknownTool = "unknown_tool";
}

/// <summary>Supuestos y advertencias de honestidad que acompañan a los resultados (CLAUDE.md §1 y §5).</summary>
public static class ToolAssumptions
{
    public const string Expected = "Amounts are expected charges based on the subscriptions the user entered, not confirmed payments.";
    public const string ConstantPrices = "Projections assume constant prices, with no taxes or penalties.";
    public const string ActiveOnly = "Inactive subscriptions are excluded.";
    public const string NoConversion = "Each currency is reported separately; currencies are never combined or converted.";
    public const string DeclaredUsage = "Usage is declared by the user, not verified.";
    public const string NothingCancelled = "The simulation does not cancel anything and savings are not guaranteed.";

    /// <summary>Clave usada cuando un texto no tiene clave registrada; la UI muestra entonces el texto en inglés.</summary>
    public const string OtherKey = "other";

    // Clave estable por supuesto (contrato con HU-36): la UI traduce por clave, nunca comparando el texto en inglés.
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        [Expected] = "expected_charges",
        [ConstantPrices] = "constant_prices",
        [ActiveOnly] = "active_only",
        [NoConversion] = "no_conversion",
        [DeclaredUsage] = "declared_usage",
        [NothingCancelled] = "nothing_cancelled",
    };

    public static string KeyOf(string assumption) => Keys.GetValueOrDefault(assumption, OtherKey);
}

/// <summary>
/// Resultado uniforme de una herramienta financiera: estado, datos, supuestos y fuentes. Se envía al modelo como
/// JSON compacto en camelCase y lo usará HU-35 como evidencia. Nunca incluye ids de suscripciones.
/// </summary>
public sealed record ToolResult(
    string Tool,
    string Status,
    object? Data,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Sources)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Cifras tipadas para la evidencia de HU-35. No se envían al modelo (ya van en <see cref="Data"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<InsightFigure> Figures { get; init; } = [];

    // Ayuda para el código C#; no se envía al modelo (el estado ya va en Status).
    [JsonIgnore]
    public bool IsOk => Status == ToolStatus.Ok;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ToolResult Invalid(string tool, string reason) =>
        new(tool, ToolStatus.InvalidArguments, new { reason }, [], []);

    public static ToolResult Unknown(string tool) =>
        new(tool, ToolStatus.UnknownTool, new { reason = "This tool does not exist. Use only the tools provided." }, [], []);
}
