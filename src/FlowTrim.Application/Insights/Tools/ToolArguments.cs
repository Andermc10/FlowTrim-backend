using System.Globalization;
using System.Text.Json;

namespace FlowTrim.Application.Insights.Tools;

/// <summary>Argumentos inválidos pedidos por el modelo; se convierte en <see cref="ToolStatus.InvalidArguments"/>.</summary>
public sealed class ToolArgumentException(string reason) : Exception(reason);

/// <summary>
/// Lector de los argumentos JSON que envía el modelo. Valida tipos y rangos; ignora propiedades desconocidas y
/// aplica valores por defecto a los opcionales. Un argumento vacío equivale a <c>{}</c>.
/// </summary>
public sealed class ToolArguments
{
    private readonly JsonElement _root;

    private ToolArguments(JsonElement root) => _root = root;

    public static ToolArguments Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ToolArguments(JsonDocument.Parse("{}").RootElement.Clone());
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ToolArgumentException("Arguments must be a valid JSON object.");
        }

        return root.ValueKind == JsonValueKind.Object
            ? new ToolArguments(root)
            : throw new ToolArgumentException("Arguments must be a JSON object.");
    }

    public int OptionalInt(string name, int min, int max, int defaultValue)
    {
        if (!TryGet(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
        {
            throw new ToolArgumentException($"{name} must be an integer between {min} and {max}.");
        }

        return number;
    }

    public IReadOnlyList<int>? OptionalIntArray(string name, int min, int max, int maxItems)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is 0 || value.GetArrayLength() > maxItems)
        {
            throw new ToolArgumentException($"{name} must be a list of 1 to {maxItems} integers between {min} and {max}.");
        }

        return value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) && number >= min && number <= max
                ? number
                : throw new ToolArgumentException($"{name} must be a list of 1 to {maxItems} integers between {min} and {max}."))
            .ToList();
    }

    public string RequiredString(string name, int maxLength)
    {
        if (!TryGet(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Trim().Length > maxLength)
        {
            throw new ToolArgumentException($"{name} is required (text up to {maxLength} characters).");
        }

        return value.GetString()!.Trim();
    }

    public IReadOnlyList<string> RequiredStringArray(string name, int maxItems, int maxLength)
    {
        var message = $"{name} must be a list of 1 to {maxItems} non-empty names (up to {maxLength} characters each).";
        if (!TryGet(name, out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is 0 || value.GetArrayLength() > maxItems)
        {
            throw new ToolArgumentException(message);
        }

        return value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
                && item.GetString()!.Trim().Length <= maxLength
                ? item.GetString()!.Trim()
                : throw new ToolArgumentException(message))
            .ToList();
    }

    public DateOnly? OptionalDate(string name)
    {
        if (!TryGet(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ToolArgumentException($"{name} must be a date in yyyy-MM-dd format.");
    }

    // Un valor null explícito cuenta como ausente (los modelos a veces envían "campo": null).
    private bool TryGet(string name, out JsonElement value) =>
        _root.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
}
