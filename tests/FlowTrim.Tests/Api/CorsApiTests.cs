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

    [Fact]
    public async Task Cors_AllowedOrigin_ExposesHeadersTheFrontendReads()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", FlowTrimApiFactory.TestFrontendOrigin);

        var response = await _factory.CreateClient().SendAsync(request);

        // Referencia de error (HU-41), espera de Insights (HU-36) y nombre del CSV exportado (HU-52).
        var exposed = string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Correlation-ID", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry-After", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Disposition", exposed, StringComparison.OrdinalIgnoreCase);
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

        // Ver Startup_WithoutAuthority_Fails: aquí solo se exige que el host no arranque.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
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

        // El mensaje (que no imprime el valor) se verifica en ConfigurationValidationTests.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
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
