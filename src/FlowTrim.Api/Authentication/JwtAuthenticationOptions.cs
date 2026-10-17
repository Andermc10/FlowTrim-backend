namespace FlowTrim.Api.Authentication;

/// <summary>Configuración del proveedor OIDC/JWT validado por la API.</summary>
public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;
}
