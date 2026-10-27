using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Calendar;
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

    [Fact]
    public async Task DailyTotals_GroupByDayAndAccountCurrency_WithoutTransfersOrOtherOwners()
    {
        // EP-14 HU-75: el calendario suma lo registrado por día y moneda en SQL.
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
                Tx(OwnerA, TransactionType.Expense, bank, null, food, 100m, 10, 2, null),
                Tx(OwnerA, TransactionType.Expense, cash, null, food, 50m, 10, 2, null),
                Tx(OwnerA, TransactionType.Income, bank, null, salary, 1000m, 10, 2, null),
                Tx(OwnerA, TransactionType.Expense, dollars, null, food, 7m, 10, 2, null),
                Tx(OwnerA, TransactionType.Transfer, bank, cash, null, 30m, 10, 2, null),
                Tx(OwnerA, TransactionType.Expense, bank, null, food, 9m, 9, 30, null),
                Tx(OwnerB, TransactionType.Expense, foreignBank, null, foreignFood, 999m, 10, 2, null),
            ]);

        using var scope = _services.CreateScope();
        var totals = await scope.ServiceProvider.GetRequiredService<ITransactionRepository>()
            .GetDailyTotalsAsync(OwnerA, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);

        Assert.Equal(
            [
                new DailyTotals(new DateOnly(2026, 10, 2), "COP", 1000m, 150m),
                new DailyTotals(new DateOnly(2026, 10, 2), "USD", 0m, 7m),
            ],
            totals);
    }

    [Fact]
    public async Task AddImported_SeesExistingMovementsOfTheAccountAndRollsBackOnFailure()
    {
        // EP-16 HU-79: la selección recibe los movimientos de la cuenta en el rango; un fallo no deja nada creado.
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var cash = Account.Create(OwnerA, "Cash", AccountType.Cash, "COP", 0m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        await SeedAsync([bank, cash], [food],
        [
            Tx(OwnerA, TransactionType.Expense, bank, null, food, 10m, 10, 5, "Exito"),
            Tx(OwnerA, TransactionType.Expense, cash, null, food, 10m, 10, 5, "Exito"),
        ]);

        using (var scope = _services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            IReadOnlyList<Transaction> seen = [];
            await repository.AddImportedAsync(OwnerA, bank.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), existing =>
            {
                seen = existing;
                return [Tx(OwnerA, TransactionType.Expense, bank, null, food, 20m, 10, 6, "Market")];
            }, default);
            Assert.Equal(["Exito"], seen.Select(t => t.Description));

            await Assert.ThrowsAnyAsync<Exception>(() => repository.AddImportedAsync(
                OwnerA, bank.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), _ =>
                [
                    Tx(OwnerA, TransactionType.Expense, bank, null, food, 30m, 10, 7, "Ok"),
                    Transaction.Create(OwnerA, TransactionType.Expense, Guid.NewGuid(), null, food.Id, 40m, new DateOnly(2026, 10, 7), "Broken", null, Clock),
                ], default));
        }

        await using var check = _database.CreateDbContext();
        Assert.Equal(["Exito", "Exito", "Market"], check.Transactions.Select(t => t.Description).OrderBy(d => d).ToList());
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
