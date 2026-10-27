using FlowTrim.Application.Imports;
using FlowTrim.Application.Reports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Imports;

/// <summary>HU-43: cabecera, reglas por campo, límites y compatibilidad con el CSV exportado (HU-52).</summary>
public sealed class SubscriptionCsvImportTests
{
    private const string Header = "name,amount,currency,billing_cycle,next_billing_date";

    // Hoy en el calendario del producto para estas pruebas: las fechas de las filas son posteriores.
    private static readonly DateOnly Today = new(2026, 1, 1);

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

    [Fact]
    public void Parse_NewCyclesKindAndPaymentUrl_AreAccepted()
    {
        // EP-14 HU-72 FR-4: ciclos nuevos y columnas opcionales kind y payment_url.
        var preview = Preview(
            $"{Header},kind,payment_url\n" +
            "Rent,1200000,COP,quarterly,2026-02-01,bill,https://pay.example.com\n" +
            "Cleaning,10000,COP,Biweekly,2026-02-01,,\n");

        Assert.Equal(
            new ImportRowValues("Rent", null, 1200000m, "COP", "Quarterly", new DateOnly(2026, 2, 1), "Active", "Bill", "https://pay.example.com"),
            preview.Rows[0].Values);
        Assert.Equal(
            new ImportRowValues("Cleaning", null, 10000m, "COP", "Biweekly", new DateOnly(2026, 2, 1), "Active"),
            preview.Rows[1].Values);
        Assert.Empty(preview.IgnoredColumns);
    }

    [Theory]
    [InlineData("Loan,https://example.com", "kind", ImportErrorCodes.InvalidKind)]
    [InlineData("Instalment,", "kind", ImportErrorCodes.InvalidKind)]
    [InlineData("Bill,http://example.com", "payment_url", ImportErrorCodes.InvalidPaymentUrl)]
    [InlineData("Bill,https://user:pass@example.com", "payment_url", ImportErrorCodes.InvalidPaymentUrl)]
    public void Parse_InvalidKindOrPaymentUrl_ReportsFieldAndCode(string cells, string field, string code)
    {
        var row = Assert.Single(Preview($"{Header},kind,payment_url\nRent,100,COP,Monthly,2026-02-01,{cells}").Rows);

        var error = Assert.Single(row.Errors);
        Assert.Equal((field, code), (error.Field, error.Code));
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
    [InlineData("Netflix,100,COP,Daily,2026-10-22", "billing_cycle", ImportErrorCodes.InvalidBillingCycle)]
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
        var row = Assert.Single(Preview($"{Header},status\n{name},-5,pesos,Daily,tomorrow,Paused").Rows);

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
    public void Parse_BlankLines_KeepSpreadsheetRowNumbers()
    {
        var preview = Preview($"{Header}\r\n\r\nNetflix,100,COP,Monthly,2026-01-01\r\n");

        // La fila en blanco cuenta en la hoja (fila 2) pero no se valida; hoy es una fecha válida.
        var row = Assert.Single(preview.Rows);
        Assert.Equal((3, ImportRowStatus.Valid), (row.RowNumber, row.Status));
    }

    [Fact]
    public void Parse_NextBillingDateBeforeToday_IsDateInPast()
    {
        var row = Assert.Single(Preview($"{Header}\nNetflix,100,COP,Monthly,2025-12-31").Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        var error = Assert.Single(row.Errors);
        Assert.Equal(("next_billing_date", ImportErrorCodes.DateInPast), (error.Field, error.Code));
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
        var result = SubscriptionCsvImport.Parse(text, Today);

        Assert.Null(result.Preview);
        Assert.Equal(code, result.FileError!.Code);
        Assert.False(result.FileError.TooLarge);
    }

    [Fact]
    public void Parse_MissingColumns_NamesThem()
    {
        var error = SubscriptionCsvImport.Parse("name,currency,next_billing_date", Today).FileError!;

        Assert.Equal("Missing required columns: amount, billing_cycle.", error.Detail);
    }

    [Fact]
    public void Parse_TooManyRows_IsTooLarge()
    {
        var rows = string.Concat(Enumerable.Repeat("Netflix,100,COP,Monthly,2026-10-22\n", SubscriptionCsvImport.MaxRows + 1));

        var error = SubscriptionCsvImport.Parse($"{Header}\n{rows}", Today).FileError!;

        Assert.Equal((ImportErrorCodes.TooManyRows, true), (error.Code, error.TooLarge));
        Assert.NotNull(SubscriptionCsvImport.Parse($"{Header}\n{rows[..^"Netflix,100,COP,Monthly,2026-10-22\n".Length]}", Today).Preview);
    }

    // ----- HU-58: archivos de hoja de cálculo -----

    [Theory]
    [InlineData("sep=,\r\nname,amount", ',', "name,amount")]
    [InlineData("SEP=;\nname;amount", ';', "name;amount")]
    [InlineData("name;amount;currency\r\n", ';', "name;amount;currency\r\n")]
    [InlineData("name,amount;x\r\n", ',', "name,amount;x\r\n")]
    public void Detect_SeparatorHintOrHeader_PicksTheDelimiter(string text, char delimiter, string content)
    {
        var (dialect, rest) = CsvDialect.Detect(text);

        Assert.Equal(delimiter, dialect!.Delimiter);
        Assert.Equal(content, rest);
    }

    [Fact]
    public void Parse_UnsupportedSeparatorHint_IsAFileError()
    {
        var error = SubscriptionCsvImport.Parse($"sep=|\n{Header}", Today).FileError!;

        Assert.Equal(ImportErrorCodes.UnsupportedDelimiter, error.Code);
    }

    [Fact]
    public void Parse_FileSavedByExcelInSpanish_IsValid()
    {
        // Excel con configuración de Colombia: ";" como separador, coma decimal y fechas día/mes/año.
        var preview = Preview(
            "name;amount;currency;billing_cycle;next_billing_date;category\r\n" +
            "Netflix;38900;COP;Monthly;05/11/2026;Streaming\r\n" +
            "\"Café, Plus\";16,99;USD;Yearly;2026-12-01;\r\n" +
            "Gym;90000.5;COP;Monthly;7/1/2027;Health\r\n");

        Assert.All(preview.Rows, row => Assert.Equal(ImportRowStatus.Valid, row.Status));
        Assert.Equal(
            [("Netflix", 38900m, new DateOnly(2026, 11, 5)), ("Café, Plus", 16.99m, new DateOnly(2026, 12, 1)), ("Gym", 90000.5m, new DateOnly(2027, 1, 7))],
            preview.Rows.Select(row => (row.Values!.Name, row.Values.Amount, row.Values.NextBillingDate)));
    }

    [Theory]
    [InlineData("Netflix;38.900,5;COP;Monthly;05/11/2026", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix;16,999;COP;Monthly;05/11/2026", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("Netflix;100;COP;Monthly;31/02/2026", "next_billing_date", ImportErrorCodes.InvalidDate)]
    public void Parse_SemicolonFile_StillRejectsAmbiguousValues(string line, string field, string code)
    {
        var row = Assert.Single(Preview($"name;amount;currency;billing_cycle;next_billing_date\n{line}").Rows);

        var error = Assert.Single(row.Errors);
        Assert.Equal((field, code), (error.Field, error.Code));
    }

    [Fact]
    public void Parse_CommaFile_KeepsStrictNumbersAndIsoDates()
    {
        var row = Assert.Single(Preview($"{Header}\nNetflix,100,COP,Monthly,05/11/2026").Rows);

        Assert.Equal(ImportErrorCodes.InvalidDate, Assert.Single(row.Errors).Code);
    }

    [Fact]
    public void Parse_SeparatorHint_IsNotARow_HeaderStaysRowOne()
    {
        var row = Assert.Single(Preview($"sep=,\r\n{Header}\r\nNetflix,100,COP,Monthly,2026-11-05\r\n").Rows);

        // Excel tampoco muestra la línea sep=: la primera fila de datos es la 2.
        Assert.Equal((2, ImportRowStatus.Valid), (row.RowNumber, row.Status));
    }

    private static SubscriptionImportPreview Preview(string text)
    {
        var result = SubscriptionCsvImport.Parse(text, Today);
        Assert.Null(result.FileError);
        return result.Preview!;
    }
}
