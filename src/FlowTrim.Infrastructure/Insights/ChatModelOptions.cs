using Microsoft.Extensions.Options;

namespace FlowTrim.Infrastructure.Insights;

/// <summary>Configuración del proveedor de IA (<c>Insights:Provider</c>). Por defecto, el proveedor simulado.</summary>
public sealed class ChatModelOptions
{
    public const string SectionName = "Insights:Provider";
    public const string FakeKind = "Fake";
    public const string OpenAiCompatibleKind = "OpenAiCompatible";

    public string Kind { get; set; } = FakeKind;

    /// <summary>Base del API compatible con OpenAI; se le añade <c>/chat/completions</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Secreto: solo User Secrets o variables de entorno, nunca en archivos versionados.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxOutputTokens { get; set; } = 400;

    public int MaxInputCharacters { get; set; } = 12000;

    public bool UsesFake => string.Equals(Kind, FakeKind, StringComparison.Ordinal);
}

/// <summary>
/// Valida la configuración al arrancar. Los mensajes nombran el ajuste y nunca imprimen su valor (la clave es
/// secreta y la URL o el modelo no aportan nada al diagnóstico).
/// </summary>
public sealed class ChatModelOptionsValidator : IValidateOptions<ChatModelOptions>
{
    private const string Prefix = ChatModelOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, ChatModelOptions options)
    {
        var errors = FindErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> FindErrors(ChatModelOptions options)
    {
        var errors = new List<string>();

        if (options.Kind is not (ChatModelOptions.FakeKind or ChatModelOptions.OpenAiCompatibleKind))
        {
            errors.Add($"{Prefix}:Kind must be {ChatModelOptions.FakeKind} or {ChatModelOptions.OpenAiCompatibleKind}.");
            return errors;
        }

        if (options.TimeoutSeconds is < 1 or > 60)
        {
            errors.Add($"{Prefix}:TimeoutSeconds must be between 1 and 60.");
        }

        if (options.MaxOutputTokens is < 1 or > 2000)
        {
            errors.Add($"{Prefix}:MaxOutputTokens must be between 1 and 2000.");
        }

        if (options.MaxInputCharacters is < 100 or > 100_000)
        {
            errors.Add($"{Prefix}:MaxInputCharacters must be between 100 and 100000.");
        }

        if (options.UsesFake)
        {
            return errors;
        }

        // Solo el adaptador real necesita destino, modelo y clave.
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{Prefix}:BaseUrl must be an absolute https:// URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            errors.Add($"{Prefix}:Model is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            errors.Add($"{Prefix}:ApiKey is required (User Secrets or environment variable).");
        }

        return errors;
    }
}
