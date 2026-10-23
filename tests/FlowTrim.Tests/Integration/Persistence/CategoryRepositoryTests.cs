using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Categories;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>Repositorio real de categorías contra PostgreSQL (HU-65): set por defecto idempotente, uso y borrado.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CategoryRepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public CategoryRepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task ListHandler_ConcurrentFirstRequests_CreateDefaultsOnce()
    {
        // Dos primeras peticiones simultáneas del mismo usuario: ON CONFLICT DO NOTHING evita duplicados.
        var requests = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _services.CreateAsyncScope();
            var handler = new ListCategoriesHandler(
                scope.ServiceProvider.GetRequiredService<ICategoryRepository>(), Clock, new TestCurrentUser(OwnerA));
            return await handler.HandleAsync(null, true, CategoryLanguage.English, default);
        });

        var results = await Task.WhenAll(requests);

        await using var check = _services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<ICategoryRepository>().ListAsync(OwnerA, null, true, default);
        Assert.Equal(14, stored.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess));
    }

    [Fact]
    public async Task ListNameExistsAndUsedIds_AreOwnerScoped()
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(OwnerA, "Salary", CategoryKind.Income, "green", "salary", Clock);
        var archived = Category.Create(OwnerA, "Old", CategoryKind.Expense, "slate", "other", Clock);
        archived.Update("Old", CategoryKind.Expense, "slate", "other", isArchived: true, Clock);
        await repository.AddAsync(food, default);
        await repository.AddAsync(salary, default);
        await repository.AddAsync(archived, default);
        await repository.AddAsync(Category.Create(OwnerB, "Food", CategoryKind.Expense, "orange", "food", Clock), default);
        await AddExpenseAsync(OwnerA, food.Id);

        Assert.Equal(["Food", "Salary"], (await repository.ListAsync(OwnerA, null, false, default)).Select(c => c.Name));
        Assert.Equal(["Food", "Old"], (await repository.ListAsync(OwnerA, CategoryKind.Expense, true, default)).Select(c => c.Name));
        Assert.True(await repository.NameExistsAsync(OwnerA, CategoryKind.Expense, "FOOD", null, default));
        Assert.False(await repository.NameExistsAsync(OwnerA, CategoryKind.Income, "food", null, default));
        Assert.False(await repository.NameExistsAsync(OwnerA, CategoryKind.Expense, "food", food.Id, default));
        Assert.Equal([food.Id], await repository.GetUsedIdsAsync(OwnerA, default));
        Assert.Empty(await repository.GetUsedIdsAsync(OwnerB, default));
    }

    [Fact]
    public async Task Delete_ReportsOutcome_AndDuplicateRaceIsANameError()
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var food = Category.Create(OwnerA, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var gifts = Category.Create(OwnerA, "Gifts", CategoryKind.Expense, "pink", "gifts", Clock);
        await repository.AddAsync(food, default);
        await repository.AddAsync(gifts, default);
        await AddExpenseAsync(OwnerA, food.Id);

        Assert.Equal(CategoryDeleteOutcome.InUse, await repository.DeleteAsync(OwnerA, food.Id, default));
        Assert.Equal(CategoryDeleteOutcome.NotFound, await repository.DeleteAsync(OwnerB, gifts.Id, default));
        Assert.Equal(CategoryDeleteOutcome.Deleted, await repository.DeleteAsync(OwnerA, gifts.Id, default));

        await using var other = _services.CreateAsyncScope();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => other.ServiceProvider
            .GetRequiredService<ICategoryRepository>()
            .AddAsync(Category.Create(OwnerA, "food", CategoryKind.Expense, "red", "food", Clock), default));
        Assert.Equal("name", exception.PropertyName);
    }

    private async Task AddExpenseAsync(string owner, Guid categoryId)
    {
        var account = Account.Create(owner, $"Bank {Guid.NewGuid():N}", AccountType.Bank, "COP", 0m, Clock);
        await using var dbContext = _database.CreateDbContext();
        dbContext.Add(account);
        dbContext.Add(Transaction.Create(
            owner, TransactionType.Expense, account.Id, null, categoryId, 10m, new DateOnly(2026, 10, 20), null, null, Clock));
        await dbContext.SaveChangesAsync();
    }
}
