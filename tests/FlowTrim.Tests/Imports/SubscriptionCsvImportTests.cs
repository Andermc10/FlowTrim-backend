using FlowTrim.Application.Imports;
using FlowTrim.Application.Reports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Imports;

/// <summary>HU-43: cabecera, reglas por campo, límites y compatibilidad con el CSV exportado (HU-52).</summary>
public sealed class SubscriptionCsvImportTests
{
    private const string Header = "name,amount,currency,billing_cycle,next_billing_date";

    [Fact]
    public void Parse_ValidRow_NormalizesValues()
    {
        var preview = Preview(
            "Name , AMOUNT,currency,Billing_Cycle,next_billing_date,category,status\r\n" +
            "  Netflix ,38900,cop,monthly,2026-10-22,  Streaming ,inactive\r\n");

        var row = Assert.Single(preview.Rows);
        Assert.Equal(ImportRowStatus.Valid, row.Status);
        Assert.Equal(2, row.RowNumber);
        Assert.Empty(row.Errors);
        Assert.Equal(new ImportRowValues("Netflix", "Streaming", 38900m, "COP", "Monthly", new DateOnly(2026, 10, 22), "Inactive"), row.Values);
        Assert.Equal(new ImportSummary(1, 1, 0, 0), preview.Summary);
    }

    [Fact]
    public void Parse_OptionalColumnsMissing_DefaultsToActiveWithoutCategory()
    {
        var row = Assert.Single(Preview($"{Header}\nSpotify,16900.5,USD,Yearly,2026-01-31").Rows);

        Assert.Equal(new ImportRowValues("Spotify", null, 16900.5m, "USD", "Yearly", new DateOnly(2026, 1, 31), "Active"), row.Values);
    }

    [Theory]
    [InlineData("Netflix,38.900.5,COP,Monthly,2026-10-22", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix,\"38,9\",COP,Monthly,2026-10-22", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix,$100,COP,Monthly,2026-10-22", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix,10.123,COP,Monthly,2026-10-22", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix,0,COP,Monthly,2026-10-22", "amount", ImportErrorCodes.AmountNotPositive)]
    [InlineData("Netflix,0.00,COP,Monthly,2026-10-22", "amount", ImportErrorCodes.AmountNotPositive)]
    [InlineData("Netflix,100,CO,Monthly,2026-10-22", "currency", ImportErrorCodes.InvalidCurrency)]
    [InlineData("Netflix,100,C0P,Monthly,2026-10-22", "currency", ImportErrorCodes.InvalidCurrency)]
    [InlineData("Netflix,100,COP,Weekly,2026-10-22", "billing_cycle", ImportErrorCodes.InvalidBillingCycle)]
    [InlineData("Netflix,100,COP,1,2026-10-22", "billing_cycle", ImportErrorCodes.InvalidBillingCycle)]
    [InlineData("Netflix,100,COP,Monthly,22/10/2026", "next_billing_date", ImportErrorCodes.InvalidDate)]
    [InlineData("Netflix,100,COP,Monthly,2026-02-30", "next_billing_date", ImportErrorCodes.InvalidDate)]
    [InlineData(",100,COP,Monthly,2026-10-22", "name", ImportErrorCodes.Required)]
    public void Parse_InvalidField_ReportsFieldAndCode(string line, string field, string code)
    {
        var row = Assert.Single(Preview($"{Header}\r\n{line}\r\n").Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Null(row.Values);
        var error = Assert.Single(row.Errors);
        Assert.Equal((field, code), (error.Field, error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Parse_RowWithSeveralInvalidFields_ReportsEveryError()
    {
        var name = new string('n', Subscription.NameMaxLength + 1);
        var row = Assert.Single(Preview($"{Header},status\n{name},-5,pesos,Weekly,tomorrow,Paused").Rows);

        Assert.Equal(
            [
                ("name", ImportErrorCodes.TooLong), ("amount", ImportErrorCodes.InvalidAmount),
                ("currency", ImportErrorCodes.InvalidCurrency), ("billing_cycle", ImportErrorCodes.InvalidBillingCycle),
                ("next_billing_date", ImportErrorCodes.InvalidDate), ("status", ImportErrorCodes.InvalidStatus),
            ],
            row.Errors.Select(error => (error.Field, error.Code)));
    }

    [Fact]
    public void Parse_RowWithWrongCellCount_IsInvalid()
    {
        var row = Assert.Single(Preview($"{Header}\nNetflix,100,COP").Rows);

        Assert.Equal(ImportErrorCodes.WrongCellCount, Assert.Single(row.Errors).Code);
    }

    [Fact]
    public void Parse_BlankLinesAndPastDates_AreHandled()
    {
        var preview = Preview($"{Header}\r\n\r\nNetflix,100,COP,Monthly,2020-01-01\r\n");

        // La fila en blanco cuenta en la hoja (fila 2) pero no se valida; una fecha pasada es válida (vencida).
        var row = Assert.Single(preview.Rows);
        Assert.Equal((3, ImportRowStatus.Valid), (row.RowNumber, row.Status));
    }

    [Fact]
    public void Parse_ExportedFile_IsValidAndRoundTrips()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        var formula = Subscription.Create(TestCurrentUser.DefaultSubject, "=Cloud, \"Pro\"", "Tools", 120m, "USD",
            BillingCycle.Yearly, new DateOnly(2026, 11, 1), clock);
        formula.Update(formula.Name, formula.Category, formula.Amount, formula.Currency, formula.BillingCycle,
            formula.NextBillingDate, SubscriptionStatus.Inactive, clock);
        var plain = Subscription.Create(TestCurrentUser.DefaultSubject, "Netflix", null, 38900m, "COP",
            BillingCycle.Monthly, new DateOnly(2026, 10, 22), clock);

        var preview = Preview(SubscriptionCsvExporter.Export([formula, plain]));

        Assert.All(preview.Rows, row => Assert.Equal(ImportRowStatus.Valid, row.Status));
        Assert.Equal(["monthly_equivalent"], preview.IgnoredColumns);
        Assert.Equal(
            new ImportRowValues("=Cloud, \"Pro\"", "Tools", 120m, "USD", "Yearly", new DateOnly(2026, 11, 1), "Inactive"),
            preview.Rows[0].Values);
        Assert.Equal(
            new ImportRowValues("Netflix", null, 38900m, "COP", "Monthly", new DateOnly(2026, 10, 22), "Active"),
            preview.Rows[1].Values);
    }

    [Fact]
    public void Parse_ValidRows_AreAcceptedByTheDomain()
    {
        var preview = Preview($"{Header},category,status\nNetflix,0.01,usd,yearly,2026-10-22,{new string('c', Subscription.CategoryMaxLength)},Active");
        var values = Assert.Single(preview.Rows).Values!;

        // La vista previa nunca acepta algo que la creación rechazaría.
        var subscription = Subscription.Create(TestCurrentUser.DefaultSubject, values.Name, values.Category, values.Amount,
            values.Currency, Enum.Parse<BillingCycle>(values.BillingCycle), values.NextBillingDate,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        Assert.Equal(values.Amount, subscription.Amount);
    }

    [Theory]
    [InlineData("", ImportErrorCodes.EmptyFile)]
    [InlineData("\r\n\r\n", ImportErrorCodes.EmptyFile)]
    [InlineData("name,currency,billing_cycle,next_billing_date\r\n", ImportErrorCodes.MissingColumns)]
    [InlineData("name,amount,Amount,currency,billing_cycle,next_billing_date\r\n", ImportErrorCodes.DuplicateColumns)]
    [InlineData("name,amount,currency,billing_cycle,next_billing_date\r\n\"Netflix,100", ImportErrorCodes.MalformedCsv)]
    public void Parse_FileProblem_ReturnsFileError(string text, string code)
    {
        var result = SubscriptionCsvImport.Parse(text);

        Assert.Null(result.Preview);
        Assert.Equal(code, result.FileError!.Code);
        Assert.False(result.FileError.TooLarge);
    }

    [Fact]
    public void Parse_MissingColumns_NamesThem()
    {
        var error = SubscriptionCsvImport.Parse("name,currency,next_billing_date").FileError!;

        Assert.Equal("Missing required columns: amount, billing_cycle.", error.Detail);
    }

    [Fact]
    public void Parse_TooManyRows_IsTooLarge()
    {
        var rows = string.Concat(Enumerable.Repeat("Netflix,100,COP,Monthly,2026-10-22\n", SubscriptionCsvImport.MaxRows + 1));

        var error = SubscriptionCsvImport.Parse($"{Header}\n{rows}").FileError!;

        Assert.Equal((ImportErrorCodes.TooManyRows, true), (error.Code, error.TooLarge));
        Assert.NotNull(SubscriptionCsvImport.Parse($"{Header}\n{rows[..^"Netflix,100,COP,Monthly,2026-10-22\n".Length]}").Preview);
    }

    private static SubscriptionImportPreview Preview(string text)
    {
        var result = SubscriptionCsvImport.Parse(text);
        Assert.Null(result.FileError);
        return result.Preview!;
    }
}
