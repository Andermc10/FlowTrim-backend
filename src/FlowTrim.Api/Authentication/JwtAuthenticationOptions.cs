namespace FlowTrim.Api.Authentication;

/// <summary>Configuración del proveedor OIDC/JWT validado por la API.</summary>
public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Claim estable que identifica al propietario de los datos.</summary>
    public const string SubjectClaimType = "sub";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Authority y Audience son obligatorios en todos los entornos.</summary>
    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(Audience);
}
