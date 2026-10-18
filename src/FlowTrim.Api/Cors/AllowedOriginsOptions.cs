namespace FlowTrim.Api.Cors;

/// <summary>Orígenes del frontend autorizados a llamar a la API desde el navegador.</summary>
public sealed class AllowedOriginsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Nombre de la política CORS registrada en el pipeline.</summary>
    public const string PolicyName = "FlowTrimFrontend";

    /// <summary>
    /// Orígenes exactos (esquema + host [+ puerto]). En producción llegan por variables de entorno
    /// <c>Cors__AllowedOrigins__0</c>, <c>__1</c>, …; nunca hay un valor por defecto en el código.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
