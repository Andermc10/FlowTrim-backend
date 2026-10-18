using FlowTrim.Api.Authentication;
using FlowTrim.Api.Cors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace FlowTrim.Tests.Api;

/// <summary>
/// Verifica de forma determinista los mensajes de validación de configuración que se registran al
/// arrancar: nombran el ajuste y nunca imprimen el valor configurado.
/// </summary>
public sealed class ConfigurationValidationTests
{
    [Fact]
    public void CorsOrigins_ProductionWithoutOrigins_FailsNamingTheSetting()
    {
        var exception = Assert.Throws<OptionsValidationException>(() => ResolveCors([], Environments.Production));

        Assert.Contains("Cors:AllowedOrigins must contain at least one origin", exception.Message);
    }

    [Theory]
    [InlineData("http://unsafe-origin.test")]
    [InlineData("https://unsafe-origin.test/app")]
    [InlineData("https://unsafe-origin.test/")]
    [InlineData("https://*.unsafe-origin.test")]
    public void CorsOrigins_ProductionUnsafeOrigin_FailsWithoutPrintingTheValue(string origin)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => ResolveCors([origin], Environments.Production));

        Assert.Contains("Cors:AllowedOrigins entry 0 is not a valid origin", exception.Message);
        Assert.DoesNotContain("unsafe-origin.test", exception.Message);
    }

    [Fact]
    public void CorsOrigins_DevelopmentLocalhost_BindsFromConfiguration()
    {
        var options = ResolveCors(["http://localhost:4200"], Environments.Development);

        Assert.Equal(["http://localhost:4200"], options.AllowedOrigins);
    }

    [Theory]
    [InlineData("", "https://api.flowtrim.dev")]
    [InlineData("https://issuer.test/", "")]
    [InlineData("  ", "  ")]
    public void JwtOptions_MissingAuthorityOrAudience_IsNotConfigured(string authority, string audience)
    {
        var options = new JwtAuthenticationOptions { Authority = authority, Audience = audience };

        Assert.False(options.IsConfigured());
    }

    // Reproduce el mismo enlace y validador que Program.cs usa para Cors:AllowedOrigins.
    private static AllowedOriginsOptions ResolveCors(string[] origins, string environmentName)
    {
        var values = origins
            .Select((origin, index) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environmentName });
        services.AddOptions<AllowedOriginsOptions>().Bind(configuration.GetSection(AllowedOriginsOptions.SectionName));
        services.AddSingleton<IValidateOptions<AllowedOriginsOptions>, AllowedOriginsValidator>();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AllowedOriginsOptions>>().Value;
    }
}
