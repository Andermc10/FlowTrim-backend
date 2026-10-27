using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Statements;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Api;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Statements;

/// <summary>Estado de cuenta mensual (EP-16 HU-78): fórmula pura y contrato HTTP. "Hoy" = 2026-10-20.</summary>
public sealed class MonthlyStatementTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private readonly FlowTrimApiFactory _factory;

    public MonthlyStatementTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public void Build_SpecExample_ComputesNetSavingsRateChangeAndShares()
    {
        // AC-1: ingresos 3.000.000, gastos 1.800.000 (Comida 600.000, Arriendo 1.200.000); septiembre 1.500.000.
        var cop = Assert.Single(MonthlyStatementCalculator.Build(
            [new PeriodTotals("COP", 3000000m, 1800000m)],
            [new PeriodTotals("COP", 2800000m, 1500000m)],
            [new CategorySpending(Food, "COP", 600000m), new CategorySpending(Rent, "COP", 1200000m)],
            [new CategorySpending(Rent, "COP", 1200000m)]));

        Assert.Equal(1200000m, cop.Net);
        Assert.Equal(40.0m, cop.SavingsRate);
        Assert.Equal(20.0m, cop.ExpenseChange);
        Assert.Equal(1300000m, cop.PreviousNet);
        Assert.Equal(new StatementCategory(Rent, 1200000m, 66.7m, 1200000m), cop.TopCategories[0]);
        Assert.Equal(new StatementCategory(Food, 600000m, 33.3m, 0m), cop.TopCategories[1]);
    }

    [Fact]
    public void Build_NoIncomeOrNoPreviousExpense_LeavesRatiosNull_AndCurrenciesNeverMix()
    {
        // AC-2: cada moneda en su bloque; sin ingresos no hay tasa de ahorro; sin gasto previo no hay cambio.
        var statements = MonthlyStatementCalculator.Build(
            [new PeriodTotals("USD", 0m, 50m), new PeriodTotals("COP", 100m, 0m), new PeriodTotals("EUR", 0m, 0m)],
            [],
            [new CategorySpending(Food, "USD", 50m)],
            []);

        Assert.Equal(["COP", "USD"], statements.Select(s => s.Currency));
        Assert.Null(statements[1].SavingsRate);
        Assert.Null(statements[1].ExpenseChange);
        Assert.Empty(statements[0].TopCategories);
        Assert.Equal(100.0m, Assert.Single(statements[1].TopCategories).Share);
    }

    [Fact]
    public async Task Statement_ApiExample_IgnoresTransfersAndOtherOwners()
    {
        var owner = $"auth0|statement-{Guid.NewGuid():N}";
        var bank = Add(Account.Create(owner, "Bank", AccountType.Bank, "COP", 0m, Clock));
        var cash = Add(Account.Create(owner, "Cash", AccountType.Cash, "COP", 0m, Clock));
        var salary = Add(Category.Create(owner, "Salary", CategoryKind.Income, "green", "salary", Clock));
        var rent = Add(Category.Create(owner, "Rent", CategoryKind.Expense, "blue", "home", Clock));
        Tx(owner, TransactionType.Income, bank, null, salary, 3000000m, new DateOnly(2026, 10, 1));
        Tx(owner, TransactionType.Expense, bank, null, rent, 1200000m, new DateOnly(2026, 10, 2));
        Tx(owner, TransactionType.Transfer, bank, cash, null, 500000m, new DateOnly(2026, 10, 3));
        Tx(owner, TransactionType.Expense, bank, null, rent, 1000000m, new DateOnly(2026, 9, 2));

        var body = await _factory.CreateClientFor(owner).GetFromJsonAsync<JsonElement>("/api/statements");
        var other = await _factory.CreateClientFor($"auth0|statement-{Guid.NewGuid():N}").GetFromJsonAsync<JsonElement>("/api/statements");
        var invalid = await _factory.CreateClientFor(owner).GetAsync("/api/statements?month=10-2026");

        Assert.Equal("2026-10", body.GetProperty("month").GetString());
        Assert.Equal("2026-09", body.GetProperty("previousMonth").GetString());
        var cop = Assert.Single(body.GetProperty("currencies").EnumerateArray());
        Assert.Equal(1800000m, cop.GetProperty("net").GetDecimal());
        Assert.Equal(60.0m, cop.GetProperty("savingsRate").GetDecimal());
        Assert.Equal(20.0m, cop.GetProperty("expenseChange").GetDecimal());
        var top = Assert.Single(cop.GetProperty("topCategories").EnumerateArray());
        Assert.Equal("Rent", top.GetProperty("name").GetString());
        Assert.Equal(1000000m, top.GetProperty("previousAmount").GetDecimal());
        Assert.Empty(other.GetProperty("currencies").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private Account Add(Account account)
    {
        _factory.Accounts.Items.Add(account);
        return account;
    }

    private Category Add(Category category)
    {
        _factory.Categories.Items.Add(category);
        return category;
    }

    private void Tx(string owner, TransactionType type, Account account, Account? to, Category? category, decimal amount, DateOnly date) =>
        _factory.Accounts.Transactions.Add(
            Transaction.Create(owner, type, account.Id, to?.Id, category?.Id, amount, date, null, null, Clock));
}
