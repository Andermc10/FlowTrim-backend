using FlowTrim.Application.Imports;
using FlowTrim.Application.TransactionImports;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.TransactionImports;

/// <summary>EP-16 HU-79: lectura del CSV del banco, reglas por fila, categorías de reserva y duplicados. "Hoy" = 2026-10-20.</summary>
public sealed class BankCsvImportTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly Category Other = Category.Create("owner", "Other", CategoryKind.Expense, "slate", "other", Clock);
    private static readonly Category Food = Category.Create("owner", "Food", CategoryKind.Expense, "orange", "food", Clock);
    private static readonly Category Salary = Category.Create("owner", "Salary", CategoryKind.Income, "green", "salary", Clock);
    private static readonly TransactionImportCategories Categories = new([Other, Food, Salary], Other, Salary);

    [Fact]
    public void Parse_CommaFile_SignedAmountsBecomeExpenseOrIncome_AndCategoriesResolveOrFallBack()
    {
        // AC-3: "food" coincide con Food; "Groceries" no existe → categoría de reserva marcada.
        var preview = Parse(
            "Date,Description,Amount,Category\n" +
            "2026-10-05,Exito,-45000.50,food\n" +
            "2026-10-05,Payroll,2500000,\n" +
            "2026-10-06,Market,-12000,Groceries\n");

        Assert.Equal(new ImportSummary(3, 3, 0, 0), preview.Summary);
        Assert.Equal(
            new TransactionImportValues(new DateOnly(2026, 10, 5), "Expense", 45000.50m, "Exito", Food.Id, "Food", false),
            preview.Rows[0].Values);
        Assert.Equal("Income", preview.Rows[1].Values!.Type);
        Assert.Equal(Salary.Id, preview.Rows[1].Values!.CategoryId);
        Assert.Equal((Other.Id, true), (preview.Rows[2].Values!.CategoryId, preview.Rows[2].Values!.CategoryFallback));
    }

    [Fact]
    public void Parse_SemicolonSpreadsheetFile_AcceptsDecimalCommaAndDayMonthYear_ButNotThousandsSeparators()
    {
        // AC-1: separadores de miles no se aceptan (se documenta); la coma decimal sí.
        var preview = Parse("sep=;\ndate;description;amount\n05/10/2026;Exito;-45000,50\n06/10/2026;Payroll;2.500.000\n");

        Assert.Equal(45000.50m, preview.Rows[0].Values!.Amount);
        Assert.Equal(new DateOnly(2026, 10, 5), preview.Rows[0].Values!.Date);
        Assert.Equal(ImportErrorCodes.InvalidAmount, Assert.Single(preview.Rows[1].Errors).Code);
    }

    [Theory]
    [InlineData("2026-10-21,x,-10", "date", TransactionImportErrorCodes.DateInFuture)]
    [InlineData("2026-02-30,x,-10", "date", ImportErrorCodes.InvalidDate)]
    [InlineData(",x,-10", "date", ImportErrorCodes.Required)]
    [InlineData("2026-10-01,x,0", "amount", TransactionImportErrorCodes.AmountZero)]
    [InlineData("2026-10-01,x,$10", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("2026-10-01,x,10.123", "amount", ImportErrorCodes.InvalidAmount)]
    [InlineData("2026-10-01,x", "row", ImportErrorCodes.WrongCellCount)]
    public void Parse_InvalidRow_ReportsFieldAndCode(string line, string field, string code)
    {
        var row = Assert.Single(Parse($"date,description,amount\n{line}\n").Rows);

        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        var error = Assert.Single(row.Errors);
        Assert.Equal((field, code), (error.Field, error.Code));
    }

    [Fact]
    public void Parse_FileProblems_AreFileErrors()
    {
        Assert.Equal(ImportErrorCodes.MissingColumns, BankCsvImport.Parse("description,amount\nx,1\n", Today, Categories).Error!.Code);
        Assert.Equal(ImportErrorCodes.EmptyFile, BankCsvImport.Parse("\n\n", Today, Categories).Error!.Code);
        Assert.Equal(ImportErrorCodes.UnsupportedDelimiter, BankCsvImport.Parse("sep=|\ndate|amount\n", Today, Categories).Error!.Code);
    }

    [Fact]
    public void Classify_SameMovementInTheAccountOrEarlierRow_IsADuplicate()
    {
        // AC-2: la misma fecha, tipo, importe y descripción (sin mayúsculas ni espacios) no se crea dos veces.
        var preview = Parse("date,description,amount\n2026-10-05, EXITO ,-45000\n2026-10-06,Rent,-900000\n2026-10-06,rent,-900000\n");
        var existing = Transaction.Create("owner", TransactionType.Expense, Guid.NewGuid(), null, Food.Id, 45000m,
            new DateOnly(2026, 10, 5), "Exito", null, Clock);

        var classified = BankCsvDuplicates.Classify(preview, [existing]);

        Assert.Equal(new ImportSummary(3, 1, 0, 2), classified.Summary);
        Assert.Equal(ImportDuplicateSource.Existing, classified.Rows[0].DuplicateOf);
        Assert.Equal((ImportDuplicateSource.File, 3), (classified.Rows[2].DuplicateOf, classified.Rows[2].DuplicateOfRow));
    }

    private static TransactionImportPreview Parse(string text)
    {
        var (preview, error) = BankCsvImport.Parse(text, Today, Categories);
        Assert.Null(error);
        return preview!;
    }
}
