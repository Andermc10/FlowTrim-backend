using FlowTrim.Application.Budgets;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>EP-15 HU-76 contra PostgreSQL: gastos por categoría sumados en SQL, unicidad y cascada al borrar la categoría.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BudgetRepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public BudgetRepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task ExpensesByCategory_SumOnlyExpensesOfTheMonthPerAccountCurrency()
    {
        var bank = Account.Create(OwnerA, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var dollars = Account.Create(OwnerA, "Dollars", AccountType.Bank, "USD", 0m, Clock);
        var cash = Account.Create(OwnerA, "Cash", AccountType.Cash, "COP", 0m, Clock);
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(OwnerA, "Salary", CategoryKind.Income, "green", "salary", Clock);
        var foreignBank = Account.Create(OwnerB, "Bank", AccountType.Bank, "COP", 0m, Clock);
        var foreignFood = Category.Create(OwnerB, "Food", CategoryKind.Expense, "orange", "food", Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.AddRange(bank, dollars, cash, food, salary, foreignBank, foreignFood);
            dbContext.AddRange(
                Tx(OwnerA, TransactionType.Expense, bank, food, 100m, 10, 2),
                Tx(OwnerA, TransactionType.Expense, cash, food, 50m, 10, 31),
                Tx(OwnerA, TransactionType.Expense, dollars, food, 7m, 10, 3),
                Tx(OwnerA, TransactionType.Income, bank, salary, 1000m, 10, 3),
                Tx(OwnerA, TransactionType.Expense, bank, food, 9m, 9, 30),
                Tx(OwnerB, TransactionType.Expense, foreignBank, foreignFood, 999m, 10, 2),
                Transaction.Create(OwnerA, TransactionType.Transfer, bank.Id, cash.Id, null, 30m, new DateOnly(2026, 10, 4), null, null, Clock));
            await dbContext.SaveChangesAsync();
        }

        using var scope = _services.CreateScope();
        var expenses = await scope.ServiceProvider.GetRequiredService<ITransactionRepository>()
            .GetExpensesByCategoryAsync(OwnerA, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);

        Assert.Equal(
            [new CategoryExpense(food.Id, "COP", 150m), new CategoryExpense(food.Id, "USD", 7m)],
            expenses.OrderBy(e => e.Currency, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeleteCategory_DeletesItsBudgets_AndDuplicatesAreRejected()
    {
        // AC-3 (D5): un presupuesto es configuración; borrar la categoría lo borra.
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.Add(food);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = _services.CreateScope())
        {
            var budgets = scope.ServiceProvider.GetRequiredService<IBudgetRepository>();
            await budgets.AddAsync(Budget.Create(OwnerA, food.Id, "COP", 100m, Clock), default);
            Assert.True(await budgets.ExistsAsync(OwnerA, food.Id, "COP", default));
        }

        await using (var duplicate = _database.CreateDbContext())
        {
            duplicate.Add(Budget.Create(OwnerA, food.Id, "COP", 200m, Clock));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal("ux_budgets_owner_id_category_id_currency", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
        }

        await using var check = _database.CreateDbContext();
        await check.Categories.Where(c => c.Id == food.Id).ExecuteDeleteAsync();
        Assert.Empty(await check.Budgets.ToListAsync());
    }

    private static Transaction Tx(string owner, TransactionType type, Account account, Category category, decimal amount, int month, int day) =>
        Transaction.Create(owner, type, account.Id, null, category.Id, amount, new DateOnly(2026, month, day), null, null, Clock);
}
