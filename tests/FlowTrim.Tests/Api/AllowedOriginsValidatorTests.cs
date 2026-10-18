using FlowTrim.Api.Cors;

namespace FlowTrim.Tests.Api;

public sealed class AllowedOriginsValidatorTests
{
    [Theory]
    [InlineData("https://app.flowtrim.dev")]
    [InlineData("https://app.flowtrim.dev:8443")]
    public void FindError_HttpsOrigin_IsValidInEveryEnvironment(string origin)
    {
        Assert.Null(AllowedOriginsValidator.FindError([origin], isDevelopment: false));
        Assert.Null(AllowedOriginsValidator.FindError([origin], isDevelopment: true));
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://127.0.0.1:4200")]
    public void CorsOrigins_DevelopmentLocalhost_IsValid(string origin)
    {
        Assert.Null(AllowedOriginsValidator.FindError([origin], isDevelopment: true));
        Assert.NotNull(AllowedOriginsValidator.FindError([origin], isDevelopment: false));
    }

    [Theory]
    [InlineData("http://192.168.1.10:4200")]
    [InlineData("http://app.flowtrim.dev")]
    public void FindError_DevelopmentNonLoopbackHttp_IsInvalid(string origin)
    {
        Assert.NotNull(AllowedOriginsValidator.FindError([origin], isDevelopment: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("app.flowtrim.dev")]
    [InlineData("https://*.flowtrim.dev")]
    [InlineData("https://app.flowtrim.dev?x=1")]
    [InlineData("https://user@app.flowtrim.dev")]
    public void FindError_MalformedOrigin_IsInvalid(string origin)
    {
        Assert.NotNull(AllowedOriginsValidator.FindError([origin], isDevelopment: true));
    }

    [Fact]
    public void FindError_SecondEntryInvalid_ReportsItsIndex()
    {
        var error = AllowedOriginsValidator.FindError(["https://app.flowtrim.dev", "*"], isDevelopment: false);

        Assert.StartsWith("Cors:AllowedOrigins entry 1 ", error);
    }

    [Fact]
    public void FindError_NoOrigins_IsInvalid()
    {
        Assert.NotNull(AllowedOriginsValidator.FindError([], isDevelopment: true));
        Assert.NotNull(AllowedOriginsValidator.FindError(null, isDevelopment: false));
    }
}
