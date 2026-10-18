using System.Net;

namespace FlowTrim.Tests.Api;

/// <summary>Criterios de HU-39 sobre CORS y validación de orígenes al arrancar.</summary>
public sealed class CorsApiTests : IClassFixture<FlowTrimApiFactory>
{
    private readonly FlowTrimApiFactory _factory;

    public CorsApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Cors_AllowedOrigin_BearerPreflight_IsAllowed()
    {
        var response = await _factory.CreateClient().SendAsync(Preflight(FlowTrimApiFactory.TestFrontendOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            FlowTrimApiFactory.TestFrontendOrigin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("authorization", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://app.flowtrim.test")]
    [InlineData("http://localhost:4200")]
    public async Task Cors_UnknownOrigin_IsRejected(string origin)
    {
        var client = _factory.CreateClient();

        var preflight = await client.SendAsync(Preflight(origin));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", origin);
        var simple = await client.SendAsync(request);

        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(simple.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Startup_ProductionWithoutAllowedOrigins_Fails()
    {
        using var factory = new FlowTrimApiFactory { AllowedOrigin = "" };

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins must contain at least one origin", exception.ToString());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://unsafe-origin.test")]
    [InlineData("https://unsafe-origin.test/app")]
    [InlineData("https://unsafe-origin.test/")]
    [InlineData("http://localhost:4200")]
    public void Startup_ProductionWithUnsafeOrigin_Fails(string origin)
    {
        using var factory = new FlowTrimApiFactory { AllowedOrigin = origin };

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var text = exception.ToString();
        Assert.Contains("Cors:AllowedOrigins entry 0 is not a valid origin", text);
        // El mensaje nombra el ajuste y la posición, pero nunca imprime el valor configurado.
        Assert.DoesNotContain("unsafe-origin.test", text);
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/subscriptions");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }
}
