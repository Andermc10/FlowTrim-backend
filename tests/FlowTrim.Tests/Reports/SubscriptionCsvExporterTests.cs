using FlowTrim.Application.Reports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Reports;

public sealed class SubscriptionCsvExporterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static Subscription Make(string name, string? category = null, decimal amount = 38900m,
        BillingCycle cycle = BillingCycle.Monthly, bool active = true)
    {
        var clock = new FixedTimeProvider(FixedNow);
        var date = new DateOnly(2026, 11, 1);
        var subscription = Subscription.Create(TestCurrentUser.DefaultSubject, name, category, amount, "COP", cycle, date, clock);
        if (!active)
        {
            subscription.Update(name, category, amount, "COP", cycle, date, SubscriptionStatus.Inactive, clock);
        }

        return subscription;
    }

    // La primera línea es siempre la indicación de separador para Excel (HU-58); el resto empieza en la cabecera.
    private static string[] Lines(string csv)
    {
        var lines = csv.Split("\r\n");
        Assert.Equal("sep=,", lines[0]);
        return lines[1..];
    }

    [Fact]
    public void Export_WritesHeaderAndInvariantRowsWithCrlf()
    {
        var csv = SubscriptionCsvExporter.Export([Make("Cloud", "Storage", 120000m, BillingCycle.Yearly, active: false)]);

        var lines = Lines(csv);
        Assert.Equal(SubscriptionCsvExporter.Header, lines[0]);
        Assert.Equal("Cloud,Storage,120000.00,COP,Yearly,10000.00,2026-11-01,Inactive,Subscription,", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void Export_WithoutSubscriptions_WritesOnlyTheHeader()
    {
        Assert.Equal("sep=,\r\n" + SubscriptionCsvExporter.Header + "\r\n", SubscriptionCsvExporter.Export([]));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1 boost", "'+1 boost")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Export_EscapesFormulaInjection(string name, string expectedCell)
    {
        var row = Lines(SubscriptionCsvExporter.Export([Make(name)]))[1];

        Assert.StartsWith(expectedCell + ",", row);
    }

    [Fact]
    public void Export_QuotesCommasAndQuotes_AndKeepsAccents()
    {
        var row = Lines(SubscriptionCsvExporter.Export([Make("Música, \"Premium\"", "Diversión")]))[1];

        Assert.StartsWith("\"Música, \"\"Premium\"\"\",Diversión,", row);
    }

    [Fact]
    public void Export_EmptyCategory_IsAnEmptyCell()
    {
        var row = Lines(SubscriptionCsvExporter.Export([Make("Netflix")]))[1];

        Assert.StartsWith("Netflix,,38900.00,", row);
    }
}
