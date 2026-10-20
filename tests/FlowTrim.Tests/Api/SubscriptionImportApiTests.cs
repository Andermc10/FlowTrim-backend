using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FlowTrim.Api.Imports;
using FlowTrim.Application.Imports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowTrim.Tests.Api;

/// <summary>
/// HU-43/HU-44 por HTTP: subida multipart, errores de archivo con código, límites, duplicados del propietario y ninguna
/// persistencia.
/// </summary>
public sealed class SubscriptionImportApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Route = "/api/subscription-imports/preview";
    private const string ValidCsv =
        "name,amount,currency,billing_cycle,next_billing_date\r\nNetflix,38900,COP,Monthly,2026-10-22\r\nBroken,abc,COP,Monthly,2026-10-22\r\n";

    private readonly FlowTrimApiFactory _factory;

    public SubscriptionImportApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Preview_ValidCsv_ReturnsRowsWithoutPersistence()
    {
        var before = _factory.Subscriptions.Items.Count;

        var response = await UploadAsync(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ValidCsv)).ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var summary = body.GetProperty("summary");
        Assert.Equal((2, 1, 1), (summary.GetProperty("total").GetInt32(), summary.GetProperty("valid").GetInt32(), summary.GetProperty("invalid").GetInt32()));
        var valid = body.GetProperty("rows")[0];
        Assert.Equal("valid", valid.GetProperty("status").GetString());
        Assert.Equal(38900m, valid.GetProperty("values").GetProperty("amount").GetDecimal());
        Assert.Equal("2026-10-22", valid.GetProperty("values").GetProperty("nextBillingDate").GetString());
        Assert.Equal(before, _factory.Subscriptions.Items.Count);
    }

    [Fact]
    public async Task Preview_InvalidRows_ReturnsRowAndFieldErrors()
    {
        var body = await (await UploadAsync(Encoding.UTF8.GetBytes(ValidCsv))).Content.ReadFromJsonAsync<JsonElement>();

        var invalid = body.GetProperty("rows")[1];
        Assert.Equal((3, "invalid"), (invalid.GetProperty("rowNumber").GetInt32(), invalid.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, invalid.GetProperty("values").ValueKind);
        var error = Assert.Single(invalid.GetProperty("errors").EnumerateArray());
        Assert.Equal(("amount", "invalid_amount"), (error.GetProperty("field").GetString(), error.GetProperty("code").GetString()));
    }

    [Theory]
    [InlineData("subscriptions.xlsx", "name,amount", ImportErrorCodes.UnsupportedFileType)]
    [InlineData("subscriptions.csv", "", ImportErrorCodes.EmptyFile)]
    [InlineData("subscriptions.CSV", "name,currency\r\n", ImportErrorCodes.MissingColumns)]
    public async Task Preview_FileProblem_Returns400WithCode(string fileName, string content, string code)
    {
        var response = await UploadAsync(Encoding.UTF8.GetBytes(content), fileName);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Preview_InvalidUtf8_Returns400()
    {
        var response = await UploadAsync([0x6E, 0x61, 0x6D, 0x65, 0xC3, 0x28]);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, ImportErrorCodes.InvalidEncoding);
    }

    [Fact]
    public async Task Preview_WithoutFile_Returns400()
    {
        var client = _factory.CreateClientFor(NewOwner());
        using var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        var response = await client.PostAsync(Route, form);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, ImportErrorCodes.FileRequired);
    }

    [Fact]
    public async Task Preview_FileOverLimit_Returns413FileTooLarge()
    {
        var content = new byte[PreviewSubscriptionImportHandler.MaxFileBytes + 1];
        Array.Fill(content, (byte)'a');

        var response = await UploadAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, ImportErrorCodes.FileTooLarge);
    }

    [Fact]
    public async Task Preview_TooManyRows_Returns413TooManyRows()
    {
        var rows = string.Concat(Enumerable.Repeat("N,1,COP,Monthly,2026-10-22\n", SubscriptionCsvImport.MaxRows + 1));

        var response = await UploadAsync(Encoding.UTF8.GetBytes($"name,amount,currency,billing_cycle,next_billing_date\n{rows}"));

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, ImportErrorCodes.TooManyRows);
    }

    [Fact]
    public async Task Preview_RequestOverBodyLimit_Returns413()
    {
        var response = await UploadAsync(new byte[SubscriptionImportEndpoints.MaxRequestBytes + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Preview_Logs_OnlyCountsNeverContent()
    {
        var logs = new LogCapture();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug)));
        using var form = Form(Encoding.UTF8.GetBytes(ValidCsv), "subscriptions.csv");

        var response = await factory.CreateClientFor(NewOwner()).PostAsync(Route, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Subscription import preview", StringComparison.Ordinal));
        Assert.Equal("Subscription import preview with 2 rows (1 valid, 1 invalid, 0 duplicate)", line.Message);
        Assert.DoesNotContain("Netflix", logs.AllText());
        Assert.DoesNotContain("38900", logs.AllText());
    }

    [Fact]
    public async Task Preview_ExistingOwnerMatch_ClassifiesDuplicate()
    {
        var owner = NewOwner();
        Seed(owner, "Netflix", "COP");
        const string csv = "name,amount,currency,billing_cycle,next_billing_date\r\n" +
            "netflix ,38900,cop,Monthly,2026-10-22\r\nGym,90000,COP,Monthly,2026-10-25\r\nGYM,90000,COP,Monthly,2026-10-25\r\n";
        var before = _factory.Subscriptions.Items.Count;

        var body = await (await UploadAsync(Encoding.UTF8.GetBytes(csv), owner: owner)).Content.ReadFromJsonAsync<JsonElement>();

        var rows = body.GetProperty("rows");
        Assert.Equal(("duplicate", "existing"), (rows[0].GetProperty("status").GetString(), rows[0].GetProperty("duplicateOf").GetString()));
        Assert.False(rows[0].TryGetProperty("duplicateOfRow", out _));
        // La fila duplicada conserva sus valores normalizados para mostrar qué coincidió.
        Assert.Equal("netflix", rows[0].GetProperty("values").GetProperty("name").GetString());
        Assert.Equal("valid", rows[1].GetProperty("status").GetString());
        Assert.False(rows[1].TryGetProperty("duplicateOf", out _));
        Assert.Equal(("file", 3), (rows[2].GetProperty("duplicateOf").GetString(), rows[2].GetProperty("duplicateOfRow").GetInt32()));
        var summary = body.GetProperty("summary");
        Assert.Equal((3, 1, 0, 2), (summary.GetProperty("total").GetInt32(), summary.GetProperty("valid").GetInt32(),
            summary.GetProperty("invalid").GetInt32(), summary.GetProperty("duplicate").GetInt32()));
        Assert.Equal(before, _factory.Subscriptions.Items.Count);
    }

    [Fact]
    public async Task Preview_OtherOwnerRow_IsNotUsedForDuplicateDetection()
    {
        Seed(NewOwner(), "Spotify", "COP");

        var body = await (await UploadAsync(Encoding.UTF8.GetBytes(
            "name,amount,currency,billing_cycle,next_billing_date\nSpotify,16900,COP,Monthly,2026-10-22"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("valid", body.GetProperty("rows")[0].GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("summary").GetProperty("duplicate").GetInt32());
    }

    [Fact]
    public async Task Preview_WithoutToken_Returns401()
    {
        using var form = Form(Encoding.UTF8.GetBytes(ValidCsv), "subscriptions.csv");

        var response = await _factory.CreateClient().PostAsync(Route, form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----- Helpers -----

    private async Task<HttpResponseMessage> UploadAsync(byte[] content, string fileName = "subscriptions.csv", string? owner = null)
    {
        using var form = Form(content, fileName);
        return await _factory.CreateClientFor(owner ?? NewOwner()).PostAsync(Route, form);
    }

    private void Seed(string owner, string name, string currency) =>
        _factory.Subscriptions.Items.Add(Subscription.Create(owner, name, null, 100m, currency, BillingCycle.Monthly,
            new DateOnly(2026, 11, 1), new FixedTimeProvider(DateTimeOffset.UtcNow)));

    private static MultipartFormDataContent Form(byte[] content, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("correlationId", out _));
    }

    private static string NewOwner() => $"auth0|import-{Guid.NewGuid():N}";
}
