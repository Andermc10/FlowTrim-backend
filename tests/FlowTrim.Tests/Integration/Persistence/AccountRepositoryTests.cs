using FlowTrim.Application.Accounts;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>Repositorio real de cuentas contra PostgreSQL (HU-64): saldos sumados en SQL, nombres únicos y borrado.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountRepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2026, 10, 20);

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public AccountRepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task GetActivity_SqlSums_MatchDomainCalculator()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 1000000m, Clock);
        var cash = Account.Create(OwnerA, "Cash", AccountType.Cash, "COP", 0m, Clock);
        var unused = Account.Create(OwnerA, "Savings", AccountType.Savings, "COP", 5m, Clock);
        var foreign = Account.Create(OwnerB, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(OwnerA, "Salary", CategoryKind.Income, "green", "salary", Clock);
        var foreignFood = Category.Create(OwnerB, "Food", CategoryKind.Expense, "orange", "food", Clock);
        Transaction[] transactions =
        [
            Transaction.Create(OwnerA, TransactionType.Income, bank.Id, null, salary.Id, 2500000m, Day, null, null, Clock),
            Transaction.Create(OwnerA, TransactionType.Expense, bank.Id, null, food.Id, 38900.50m, Day, null, null, Clock),
            Transaction.Create(OwnerA, TransactionType.Transfer, bank.Id, cash.Id, null, 100000m, Day, null, null, Clock),
            Transaction.Create(OwnerA, TransactionType.Transfer, cash.Id, bank.Id, null, 20000m, Day, null, null, Clock),
            Transaction.Create(OwnerA, TransactionType.Expense, cash.Id, null, food.Id, 15000m, Day, null, null, Clock),
        ];
        var foreignExpense = Transaction.Create(
            OwnerB, TransactionType.Expense, foreign.Id, null, foreignFood.Id, 999m, Day, null, null, Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.AddRange(bank, cash, unused, foreign, food, salary, foreignFood);
            dbContext.AddRange(transactions);
            dbContext.Add(foreignExpense);
            await dbContext.SaveChangesAsync();
        }

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var all = await repository.GetActivityAsync(OwnerA, null, default);
        var onlyCash = await repository.GetActivityAsync(OwnerA, cash.Id, default);

        var expected = AccountBalanceCalculator.Summarize(transactions);
        Assert.Equal(expected.OrderBy(p => p.Key), all.OrderBy(p => p.Key));
        Assert.False(all.ContainsKey(unused.Id));
        Assert.Equal(new AccountActivity(cash.Id, 100000m, 35000m), Assert.Single(onlyCash).Value);
        Assert.Equal(3381099.50m, AccountBalanceCalculator.GetBalance(bank.OpeningBalance, all[bank.Id]));
    }

    [Fact]
    public async Task ListAndNameExists_AreOwnerScopedAndCaseInsensitive()
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var archived = Account.Create(OwnerA, "Archived", AccountType.Cash, "COP", 0m, Clock);
        archived.Update("Archived", AccountType.Cash, "COP", 0m, isArchived: true, Clock);
        await repository.AddAsync(bank, default);
        await repository.AddAsync(archived, default);
        await repository.AddAsync(Account.Create(OwnerB, "Other", AccountType.Bank, "COP", 0m, Clock), default);

        Assert.Equal(["Bank"], (await repository.ListAsync(OwnerA, false, default)).Select(a => a.Name));
        Assert.Equal(["Archived", "Bank"], (await repository.ListAsync(OwnerA, true, default)).Select(a => a.Name));
        Assert.True(await repository.NameExistsAsync(OwnerA, "BANK", null, default));
        Assert.False(await repository.NameExistsAsync(OwnerA, "bank", bank.Id, default));
        Assert.False(await repository.NameExistsAsync(OwnerB, "bank", null, default));
        Assert.Null(await repository.GetByIdAsync(OwnerB, bank.Id, default));
    }

    [Fact]
    public async Task Add_DuplicateNameRace_IsTranslatedToNameValidationError()
    {
        await using (var first = _services.CreateAsyncScope())
        {
            await first.ServiceProvider.GetRequiredService<IAccountRepository>()
                .AddAsync(Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock), default);
        }

        // Simula la segunda de dos peticiones simultáneas: se salta la comprobación previa y llega al índice único.
        await using var second = _services.CreateAsyncScope();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => second.ServiceProvider
            .GetRequiredService<IAccountRepository>()
            .AddAsync(Account.Create(OwnerA, "bank", AccountType.Cash, "COP", 0m, Clock), default));

        Assert.Equal("name", exception.PropertyName);
    }

    [Fact]
    public async Task Delete_ReportsOutcomeAndNeverDeletesAccountsWithTransactions()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var cash = Account.Create(OwnerA, "Cash", AccountType.Cash, "COP", 0m, Clock);
        var empty = Account.Create(OwnerA, "Empty", AccountType.Cash, "COP", 0m, Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.AddRange(bank, cash, empty);
            dbContext.Add(Transaction.Create(OwnerA, TransactionType.Transfer, bank.Id, cash.Id, null, 10m, Day, null, null, Clock));
            await dbContext.SaveChangesAsync();
        }

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        Assert.Equal(AccountDeleteOutcome.HasTransactions, await repository.DeleteAsync(OwnerA, cash.Id, default));
        Assert.Equal(AccountDeleteOutcome.NotFound, await repository.DeleteAsync(OwnerB, empty.Id, default));
        Assert.Equal(AccountDeleteOutcome.Deleted, await repository.DeleteAsync(OwnerA, empty.Id, default));
        Assert.Equal(["Bank", "Cash"], (await repository.ListAsync(OwnerA, true, default)).Select(a => a.Name));
    }
}
