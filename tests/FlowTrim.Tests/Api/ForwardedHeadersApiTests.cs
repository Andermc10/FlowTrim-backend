using System.Net;

namespace FlowTrim.Tests.Api;

/// <summary>
/// HU-40: detrás del proxy de Render la API recibe HTTP y el esquema real llega en X-Forwarded-Proto. Con
/// ASPNETCORE_FORWARDEDHEADERS_ENABLED la API lo respeta (HSTS y sin bucle de redirección); sin ella, lo ignora
/// para que nadie pueda hacerse pasar por HTTPS enviando la cabecera a mano.
/// </summary>
public sealed class ForwardedHeadersApiTests
{
    // Un host público cualquiera: HSTS nunca se envía a localhost.
    private const string PublicHost = "flowtrim-api.onrender.test";

    private static HttpRequestMessage HealthBehindProxy(string scheme)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = PublicHost;
        request.Headers.Add("X-Forwarded-Proto", scheme);
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");
        return request;
    }

    [Fact]
    public async Task ForwardedProtoHttps_WhenEnabled_IsTreatedAsHttpsAndSendsHsts()
    {
        using var factory = new FlowTrimApiFactory { ForwardedHeadersEnabled = true };

        var response = await factory.CreateClient().SendAsync(HealthBehindProxy("https"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ForwardedProtoHttps_WhenDisabled_IsIgnored()
    {
        using var factory = new FlowTrimApiFactory();

        var response = await factory.CreateClient().SendAsync(HealthBehindProxy("https"));

        // Sigue siendo una petición HTTP: no hay HSTS (la cabecera no se toma en cuenta sin el ajuste).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
