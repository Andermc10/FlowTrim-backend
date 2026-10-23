using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>Repositorio real de movimientos contra PostgreSQL (HU-66): filtros, orden, totales en SQL y FK.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TransactionRepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private static readonly TransactionFilter All = new(null, null, null, null, null, null);

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public TransactionRepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task FiltersOrderAndTotals_MatchTheRulesPerCurrency()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var cash = Account.Create(OwnerA, "Cash", AccountType.Cash, "COP", 0m, Clock);
        var dollars = Account.Create(OwnerA, "Dollars", AccountType.Bank, "USD", 0m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(OwnerA, "Salary", CategoryKind.Income, "green", "salary", Clock);
        var foreignBank = Account.Create(OwnerB, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var foreignFood = Category.Create(OwnerB, "Food", CategoryKind.Expense, "orange", "food", Clock);
        await SeedAsync(
            [bank, cash, dollars, foreignBank],
            [food, salary, foreignFood],
            [
                Tx(OwnerA, TransactionType.Expense, bank, null, food, 100m, 9, 30, "September"),
                Tx(OwnerA, TransactionType.Expense, bank, null, food, 200.25m, 10, 1, "100% Market"),
                Tx(OwnerA, TransactionType.Expense, cash, null, food, 300m, 10, 15, "Lunch"),
                Tx(OwnerA, TransactionType.Income, bank, null, salary, 5000m, 10, 5, "Pay"),
                Tx(OwnerA, TransactionType.Expense, dollars, null, food, 12.5m, 10, 8, null),
                Tx(OwnerA, TransactionType.Transfer, bank, cash, null, 50m, 10, 6, null),
                Tx(OwnerB, TransactionType.Expense, foreignBank, null, foreignFood, 999m, 10, 10, "Lunch"),
            ]);

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var october = All with { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31) };

        Assert.Equal(5, await repository.CountAsync(OwnerA, october, default));
        var page = await repository.ListAsync(OwnerA, october, 0, 3, default);
        Assert.Equal([15, 8, 6], page.Select(t => t.Date.Day));
        Assert.Equal(
            [new CurrencyTotals("COP", 5000m, 500.25m, 4499.75m), new CurrencyTotals("USD", 0m, 12.5m, -12.5m)],
            await repository.GetTotalsAsync(OwnerA, october, default));

        // El 50% del usuario es literal (comodines escapados) y la búsqueda no distingue mayúsculas.
        Assert.Equal(["100% Market"], (await repository.ListAsync(OwnerA, All with { Search = "0% m" }, 0, 10, default)).Select(t => t.Description));
        Assert.Empty(await repository.ListAsync(OwnerA, All with { Search = "_" }, 0, 10, default));
        Assert.Equal(["Lunch"], (await repository.ListAsync(OwnerA, All with { Search = "LUNCH" }, 0, 10, default)).Select(t => t.Description));
        Assert.Equal(
            [TransactionType.Expense, TransactionType.Transfer],
            (await repository.ListAsync(OwnerA, october with { AccountId = cash.Id }, 0, 10, default)).Select(t => t.Type));
        Assert.Equal(1, await repository.CountAsync(OwnerA, All with { Type = TransactionType.Income }, default));
        Assert.Equal(4, await repository.CountAsync(OwnerA, All with { CategoryId = food.Id }, default));
    }

    [Fact]
    public async Task AddAndDelete_AreOwnerScoped_AndAMissingAccountIsAFieldError()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        await SeedAsync([bank], [food], []);

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var expense = Tx(OwnerA, TransactionType.Expense, bank, null, food, 10m, 10, 20, null);
        await repository.AddAsync(expense, default);

        Assert.Null(await repository.GetByIdAsync(OwnerB, expense.Id, default));
        Assert.False(await repository.DeleteAsync(OwnerB, expense.Id, default));
        Assert.True(await repository.DeleteAsync(OwnerA, expense.Id, default));

        // Cuenta borrada entre la validación y el guardado: la FK compuesta lo detiene y se informa en el campo.
        await using var other = _services.CreateAsyncScope();
        var orphan = Transaction.Create(
            OwnerA, TransactionType.Expense, Guid.NewGuid(), null, food.Id, 10m, new DateOnly(2026, 10, 20), null, null, Clock);
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            other.ServiceProvider.GetRequiredService<ITransactionRepository>().AddAsync(orphan, default));
        Assert.Equal("accountId", exception.PropertyName);
    }

    private async Task SeedAsync(Account[] accounts, Category[] categories, Transaction[] transactions)
    {
        await using var dbContext = _database.CreateDbContext();
        dbContext.AddRange(accounts);
        dbContext.AddRange(categories);
        dbContext.AddRange(transactions);
        await dbContext.SaveChangesAsync();
    }

    private static Transaction Tx(
        string owner, TransactionType type, Account account, Account? to, Category? category, decimal amount,
        int month, int day, string? description) =>
        Transaction.Create(owner, type, account.Id, to?.Id, category?.Id, amount, new DateOnly(2026, month, day), description, null, Clock);
}
