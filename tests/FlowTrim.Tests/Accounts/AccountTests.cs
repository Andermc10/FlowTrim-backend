using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Accounts;

/// <summary>Invariantes de <see cref="Account"/> y saldo calculado (HU-64, EP-12 D2).</summary>
public sealed class AccountTests
{
    private const string Owner = "owner-a";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_ValidInput_NormalizesNameAndCurrency()
    {
        var account = Account.Create(Owner, "  Bank  ", AccountType.Bank, " cop ", 500000m, Clock);

        Assert.Equal("Bank", account.Name);
        Assert.Equal("COP", account.Currency);
        Assert.Equal(500000m, account.OpeningBalance);
        Assert.False(account.IsArchived);
        Assert.Equal(Clock.GetUtcNow(), account.CreatedAt);
    }

    [Fact]
    public void Create_NegativeOpeningBalance_IsAllowedForADebt()
    {
        var card = Account.Create(Owner, "Visa", AccountType.CreditCard, "COP", -300000m, Clock);

        Assert.Equal(-300000m, card.OpeningBalance);
    }

    [Theory]
    [InlineData("", "COP", 0, "name")]
    [InlineData("   ", "COP", 0, "name")]
    [InlineData("Bank", null, 0, "currency")]
    [InlineData("Bank", "CO", 0, "currency")]
    [InlineData("Bank", "C0P", 0, "currency")]
    [InlineData("Bank", "COP", 0.001, "openingBalance")]
    public void Create_InvalidInput_ThrowsForField(string name, string? currency, double openingBalance, string field)
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Account.Create(Owner, name, AccountType.Bank, currency, (decimal)openingBalance, Clock));

        Assert.Equal(field, exception.PropertyName);
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Account.Create(Owner, new string('a', Account.NameMaxLength + 1), AccountType.Cash, "COP", 0m, Clock));

        Assert.Equal("name", exception.PropertyName);
    }

    [Fact]
    public void Create_OpeningBalanceOutOfRange_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Account.Create(Owner, "Bank", AccountType.Bank, "COP", Money.MaxAbsoluteValue + 1m, Clock));

        Assert.Equal("openingBalance", exception.PropertyName);
    }

    [Fact]
    public void Create_UndefinedType_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Account.Create(Owner, "Bank", (AccountType)99, "COP", 0m, Clock));

        Assert.Equal("type", exception.PropertyName);
    }

    [Fact]
    public void Create_WithoutOwner_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Account.Create(" ", "Bank", AccountType.Bank, "COP", 0m, Clock));

        Assert.Equal("ownerId", exception.PropertyName);
    }

    [Fact]
    public void Update_ArchivesAndRefreshesUpdatedAt()
    {
        var account = Account.Create(Owner, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(1));

        account.Update("Main bank", AccountType.Savings, "USD", 10m, isArchived: true, later);

        Assert.Equal("Main bank", account.Name);
        Assert.Equal(AccountType.Savings, account.Type);
        Assert.Equal("USD", account.Currency);
        Assert.True(account.IsArchived);
        Assert.Equal(later.GetUtcNow(), account.UpdatedAt);
        Assert.Equal(Clock.GetUtcNow(), account.CreatedAt);
    }

    [Fact]
    public void GetBalance_BankWithExpense_SubtractsIt()
    {
        // AC-1 de HU-64: banco en COP con 500.000 y un gasto de 38.900 → 461.100.
        var bank = Account.Create(Owner, "Bank", AccountType.Bank, "COP", 500000m, Clock);
        var expense = Expense(bank.Id, 38900m);

        var activity = AccountBalanceCalculator.Summarize([expense]);

        Assert.Equal(461100m, AccountBalanceCalculator.GetBalance(bank.OpeningBalance, activity[bank.Id]));
    }

    [Fact]
    public void Summarize_IncomeExpenseAndTransfersBothWays_AddsAndSubtractsPerAccount()
    {
        var bank = Guid.NewGuid();
        var cash = Guid.NewGuid();

        var activity = AccountBalanceCalculator.Summarize(
        [
            Income(bank, 1000000m),
            Expense(bank, 38900m),
            Transfer(bank, cash, 100000m),
            Transfer(cash, bank, 20000m),
            Expense(cash, 15000m),
        ]);

        Assert.Equal(new AccountActivity(bank, 1020000m, 138900m), activity[bank]);
        Assert.Equal(new AccountActivity(cash, 100000m, 35000m), activity[cash]);
        Assert.Equal(1881100m, AccountBalanceCalculator.GetBalance(1000000m, activity[bank]));
        Assert.Equal(65000m, AccountBalanceCalculator.GetBalance(0m, activity[cash]));
    }

    [Fact]
    public void GetBalance_WithoutActivity_IsOpeningBalance()
    {
        Assert.Equal(-300000m, AccountBalanceCalculator.GetBalance(-300000m, null));
        Assert.Empty(AccountBalanceCalculator.Summarize([]));
    }

    [Fact]
    public void Summarize_ArchivedAccount_KeepsItsActivity()
    {
        // Archivar no cambia el historial: el saldo se sigue calculando con los mismos movimientos.
        var account = Account.Create(Owner, "Old wallet", AccountType.Cash, "COP", 50000m, Clock);
        var expense = Expense(account.Id, 10000m);
        account.Update(account.Name, account.Type, account.Currency, account.OpeningBalance, isArchived: true, Clock);

        var activity = AccountBalanceCalculator.Summarize([expense]);

        Assert.Equal(40000m, AccountBalanceCalculator.GetBalance(account.OpeningBalance, activity[account.Id]));
    }

    private static readonly DateOnly Day = new(2026, 10, 20);

    private static Transaction Income(Guid accountId, decimal amount) =>
        Transaction.Create(Owner, TransactionType.Income, accountId, null, Guid.NewGuid(), amount, Day, null, null, Clock);

    private static Transaction Expense(Guid accountId, decimal amount) =>
        Transaction.Create(Owner, TransactionType.Expense, accountId, null, Guid.NewGuid(), amount, Day, null, null, Clock);

    private static Transaction Transfer(Guid from, Guid to, decimal amount) =>
        Transaction.Create(Owner, TransactionType.Transfer, from, to, null, amount, Day, null, null, Clock);
}
