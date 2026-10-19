using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FlowTrim.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowTrim.Tests.Api;

/// <summary>
/// Cuerpos de petición ilegibles son errores del cliente: 400 ProblemDetails seguro, nunca 500 (backend#74).
/// </summary>
public sealed class MalformedRequestApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Owner = "auth0|malformed";
    private readonly FlowTrimApiFactory _factory;

    public MalformedRequestApiTests(FlowTrimApiFactory factory) => _factory = factory;

    public static TheoryData<string, byte[]> UnreadableBodies() => new()
    {
        // "ú" enviada como Latin-1 (0xFA): UTF-8 inválido, el caso real que destapó el defecto.
        { "invalid UTF-8", [.. Encoding.ASCII.GetBytes("{\"name\":\"M"), 0xFA, .. Encoding.ASCII.GetBytes("sica\",\"amount\":10}")] },
        { "broken JSON", Encoding.UTF8.GetBytes("{\"name\": \"Netflix\", ") },
        { "wrong type", Encoding.UTF8.GetBytes("{\"name\":\"Netflix\",\"amount\":\"lots\"}") },
    };

    [Theory]
    [MemberData(nameof(UnreadableBodies))]
    public async Task UnreadableBody_ReturnsBadRequestProblemWithoutDetails(string scenario, byte[] body)
    {
        var logs = new LogCapture();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, Owner);

        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        var response = await client.PostAsync("/api/subscriptions", content);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario}: got {(int)response.StatusCode}");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal(Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)),
            problem.GetProperty("correlationId").GetString());
        var raw = problem.GetRawText();
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain("CreateSubscriptionRequest", raw);
        // Un error del cliente no se registra como Error del servidor.
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }
}
