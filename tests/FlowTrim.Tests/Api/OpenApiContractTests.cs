using FlowTrim.Api.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace FlowTrim.Tests.Api;

/// <summary>
/// El documento OpenAPI es el contrato del que la app móvil genera sus tipos (EP-17 D6): toda operación debe declarar
/// su respuesta de éxito con esquema, y las rutas con sesión deben declarar el esquema Bearer y su 401.
/// </summary>
public sealed class OpenApiContractTests : IClassFixture<FlowTrimApiFactory>
{
    private const string AnonymousOperation = "GetHealth";

    private readonly FlowTrimApiFactory _factory;

    public OpenApiContractTests(FlowTrimApiFactory factory) => _factory = factory;

    // El documento se genera en memoria: no depende del entorno Development donde se publica /openapi/v1.json.
    private async Task<OpenApiDocument> GetDocumentAsync()
    {
        var provider = _factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        return await provider.GetOpenApiDocumentAsync(CancellationToken.None);
    }

    private static IEnumerable<(string Name, OpenApiOperation Operation)> Operations(OpenApiDocument document) =>
        document.Paths.Values
            .SelectMany(path => path.Operations ?? [])
            .Select(entry => (entry.Value.OperationId ?? "(no operation id)", entry.Value));

    [Fact]
    public async Task Document_EveryOperation_DeclaresSuccessResponseWithSchema()
    {
        var document = await GetDocumentAsync();

        var missing = Operations(document)
            .Where(item =>
            {
                var success = item.Operation.Responses?.Where(response => response.Key.StartsWith('2')).ToList() ?? [];
                // 204 no tiene cuerpo; cualquier otro 2xx debe describir su contenido.
                return success.Count == 0
                       || success.Any(response => response.Key != "204"
                                                  && (response.Value.Content is null || response.Value.Content.Count == 0));
            })
            .Select(item => item.Name)
            .ToList();

        Assert.True(missing.Count == 0, $"Operations without a typed success response: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Document_BearerScheme_IsDeclared()
    {
        var document = await GetDocumentAsync();

        var scheme = Assert.Contains(BearerSecurityTransformer.SchemeName, document.Components!.SecuritySchemes!);
        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);
        Assert.Equal("JWT", scheme.BearerFormat);
    }

    [Fact]
    public async Task Document_AuthorizedOperations_RequireBearerAndDeclare401()
    {
        var document = await GetDocumentAsync();

        var wrong = Operations(document)
            .Where(item => item.Name != AnonymousOperation)
            .Where(item => item.Operation.Security is not { Count: > 0 }
                           || item.Operation.Responses?.ContainsKey("401") != true)
            .Select(item => item.Name)
            .ToList();

        Assert.True(wrong.Count == 0, $"Authorized operations without bearer or 401: {string.Join(", ", wrong)}");
    }

    // Veracidad: los campos que la API devuelve de verdad deben existir en el esquema declarado.
    [Fact]
    public async Task TodayDashboard_RealResponse_MatchesDeclaredSchema()
    {
        var document = await GetDocumentAsync();
        var operation = Assert.Single(Operations(document), item => item.Name == "GetTodayDashboard").Operation;
        var schema = operation.Responses!["200"].Content!["application/json"].Schema!;

        var response = await _factory.CreateClientFor("auth0|openapi-owner").GetAsync("/api/dashboard/today");
        response.EnsureSuccessStatusCode();
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var declared = schema.Properties!.Keys.ToHashSet();
        var actual = body.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.NotEmpty(actual);
        Assert.All(actual, name => Assert.Contains(name, declared));
    }

    [Fact]
    public async Task Document_HealthOperation_IsAnonymous()
    {
        var document = await GetDocumentAsync();

        var health = Assert.Single(Operations(document), item => item.Name == AnonymousOperation).Operation;
        Assert.True(health.Security is null or { Count: 0 });
    }
}
