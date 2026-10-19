using Microsoft.Extensions.Options;

namespace FlowTrim.Api.Insights;

/// <summary>Límites del endpoint de preguntas (HU-35). El proveedor se configura aparte en <c>Insights:Provider</c>.</summary>
public sealed class InsightsOptions
{
    public const string SectionName = "Insights";

    /// <summary>Tiempo total por pregunta (todas las llamadas al modelo); cada llamada conserva su propio timeout.</summary>
    public int RequestTimeoutSeconds { get; set; } = 45;

    public InsightsRateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>Ventana fija por propietario (<c>sub</c>), sin cola.</summary>
public sealed class InsightsRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Valida los límites al arrancar; los mensajes nombran el ajuste y el rango, no el valor.</summary>
public sealed class InsightsOptionsValidator : IValidateOptions<InsightsOptions>
{
    public ValidateOptionsResult Validate(string? name, InsightsOptions options)
    {
        var errors = new List<string>();
        if (options.RequestTimeoutSeconds is < 5 or > 120)
        {
            errors.Add("Insights:RequestTimeoutSeconds must be between 5 and 120.");
        }

        if (options.RateLimit.PermitLimit is < 1 or > 100)
        {
            errors.Add("Insights:RateLimit:PermitLimit must be between 1 and 100.");
        }

        if (options.RateLimit.WindowSeconds is < 10 or > 3600)
        {
            errors.Add("Insights:RateLimit:WindowSeconds must be between 10 and 3600.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
