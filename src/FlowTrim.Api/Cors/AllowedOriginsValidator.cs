using Microsoft.Extensions.Options;

namespace FlowTrim.Api.Cors;

/// <summary>
/// Valida los orígenes CORS al arrancar. Fuera de desarrollo solo se aceptan orígenes <c>https://</c>
/// exactos; en desarrollo también <c>http://localhost</c>. Los mensajes nombran el ajuste y la posición,
/// nunca el valor configurado.
/// </summary>
public sealed class AllowedOriginsValidator(IHostEnvironment environment) : IValidateOptions<AllowedOriginsOptions>
{
    private const string SettingName = "Cors:AllowedOrigins";

    public ValidateOptionsResult Validate(string? name, AllowedOriginsOptions options)
    {
        var error = FindError(options.AllowedOrigins, environment.IsDevelopment());
        return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
    }

    /// <summary>Devuelve el primer error encontrado, o <c>null</c> si todos los orígenes son válidos.</summary>
    public static string? FindError(IReadOnlyList<string>? origins, bool isDevelopment)
    {
        if (origins is null || origins.Count == 0)
        {
            return $"{SettingName} must contain at least one origin.";
        }

        for (var index = 0; index < origins.Count; index++)
        {
            if (!IsValidOrigin(origins[index], isDevelopment))
            {
                return $"{SettingName} entry {index} is not a valid origin: use an absolute https:// origin " +
                       "(scheme and host, optional port) without path, trailing slash or wildcard.";
            }
        }

        return null;
    }

    private static bool IsValidOrigin(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*') || value.EndsWith('/'))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        // http solo se permite en desarrollo y únicamente contra la máquina local.
        return uri.Scheme == Uri.UriSchemeHttps ||
               (isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
