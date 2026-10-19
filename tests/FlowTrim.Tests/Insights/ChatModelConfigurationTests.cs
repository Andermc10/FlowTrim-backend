using FlowTrim.Application.Insights;
using FlowTrim.Infrastructure.Insights;
using FlowTrim.Tests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Insights;

/// <summary>Opciones del proveedor de IA y selección de implementación al arrancar (HU-33).</summary>
public sealed class ChatModelConfigurationTests
{
    private static ChatModelOptions ValidReal() => new()
    {
        Kind = ChatModelOptions.OpenAiCompatibleKind,
        BaseUrl = "https://models.github.ai/inference",
        Model = "openai/gpt-4o-mini",
        ApiKey = "secret-value-xyz",
    };

    [Fact]
    public void Options_DefaultsToFakeAndIsValid()
    {
        var options = new ChatModelOptions();

        Assert.True(options.UsesFake);
        Assert.Empty(ChatModelOptionsValidator.FindErrors(options));
    }

    [Fact]
    public void Options_ValidRealProvider_HasNoErrors()
    {
        Assert.Empty(ChatModelOptionsValidator.FindErrors(ValidReal()));
    }

    [Theory]
    [InlineData("ApiKey", "", "Insights:Provider:ApiKey is required")]
    [InlineData("Model", " ", "Insights:Provider:Model is required")]
    [InlineData("BaseUrl", "http://models.github.ai/inference", "Insights:Provider:BaseUrl must be an absolute https:// URL")]
    [InlineData("BaseUrl", "models.github.ai", "Insights:Provider:BaseUrl must be an absolute https:// URL")]
    [InlineData("Kind", "Magic", "Insights:Provider:Kind must be Fake or OpenAiCompatible")]
    [InlineData("TimeoutSeconds", "0", "Insights:Provider:TimeoutSeconds must be between 1 and 60")]
    [InlineData("MaxOutputTokens", "5000", "Insights:Provider:MaxOutputTokens must be between 1 and 2000")]
    public void Options_InvalidSetting_FailsNamingItWithoutItsValue(string setting, string value, string expected)
    {
        var options = ValidReal();
        switch (setting)
        {
            case "ApiKey": options.ApiKey = value; break;
            case "Model": options.Model = value; break;
            case "BaseUrl": options.BaseUrl = value; break;
            case "Kind": options.Kind = value; break;
            case "TimeoutSeconds": options.TimeoutSeconds = int.Parse(value); break;
            case "MaxOutputTokens": options.MaxOutputTokens = int.Parse(value); break;
        }

        var errors = string.Join(" | ", ChatModelOptionsValidator.FindErrors(options));

        Assert.Contains(expected, errors);
        Assert.DoesNotContain("secret-value-xyz", errors);
        Assert.DoesNotContain("models.github.ai", errors);
    }

    [Fact]
    public void Startup_DefaultConfiguration_UsesFake()
    {
        using var factory = new FlowTrimApiFactory();

        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeChatModel>(scope.ServiceProvider.GetRequiredService<IChatModel>());
    }

    [Fact]
    public void Startup_RealProviderConfigured_UsesTheAdapter()
    {
        using var factory = new FlowTrimApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Insights:Provider:Kind", "OpenAiCompatible");
            builder.UseSetting("Insights:Provider:BaseUrl", "https://models.example.test/inference");
            builder.UseSetting("Insights:Provider:Model", "openai/gpt-4o-mini");
            builder.UseSetting("Insights:Provider:ApiKey", "test-key-not-real");
        });

        using var scope = factory.Services.CreateScope();

        Assert.IsType<OpenAiCompatibleChatModel>(scope.ServiceProvider.GetRequiredService<IChatModel>());
    }

    [Fact]
    public void Startup_RealProviderWithoutKey_Fails()
    {
        using var factory = new FlowTrimApiFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Insights:Provider:Kind", "OpenAiCompatible"));

        // Solo se exige que no arranque (ver HU-39: el host puede entregar otra excepción al fallar).
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
