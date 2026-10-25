using System.Net;
using System.Text;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>HU-52: exportación CSV a través de HTTP.</summary>
public sealed class CsvExportApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string Url = "/api/reports/subscriptions.csv";
    private const string OwnerA = "auth0|csv-a";
    private const string OwnerB = "auth0|csv-b";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly FlowTrimApiFactory _factory;

    public CsvExportApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Subscriptions.Items.Clear();
    }

    [Fact]
    public async Task CsvExport_TwoOwners_OnlyCurrentOwnerRows()
    {
        Seed(OwnerA, "Netflix");
        Seed(OwnerA, "Spotify");
        Seed(OwnerB, "Gym");

        var csv = await ReadCsvAsync(await _factory.CreateClientFor(OwnerA).GetAsync(Url));

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal("sep=,", lines[0]);
        Assert.StartsWith("Netflix,", lines[2]);
        Assert.StartsWith("Spotify,", lines[3]);
        Assert.DoesNotContain("Gym", csv);
    }

    [Fact]
    public async Task CsvExport_Headers_AreAttachmentUtf8WithBom()
    {
        var response = await _factory.CreateClientFor(OwnerA).GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Matches(@"^flowtrim-subscriptions-\d{4}-\d{2}-\d{2}\.csv$", disposition?.FileName?.Trim('"') ?? "");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
    }

    [Fact]
    public async Task CsvExport_WithoutSubscriptions_ReturnsOnlyTheHeader()
    {
        var csv = await ReadCsvAsync(await _factory.CreateClientFor(OwnerA).GetAsync(Url));

        Assert.Equal("sep=,\r\nname,category,amount,currency,billing_cycle,monthly_equivalent,next_billing_date,status,kind,payment_url\r\n", csv);
    }

    [Fact]
    public async Task CsvExport_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private void Seed(string ownerId, string name) =>
        _factory.Subscriptions.Items.Add(Subscription.Create(
            ownerId, name, null, 10000m, "COP", BillingCycle.Monthly, new DateOnly(2026, 11, 1), new FixedTimeProvider(FixedNow)));

    private static async Task<string> ReadCsvAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes.AsSpan(Encoding.UTF8.GetPreamble().Length));
    }
}
