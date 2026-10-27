using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>
/// EP-14 HU-73 contra PostgreSQL: el gasto y la nueva fecha se guardan juntos o ninguno (AC-2), borrar el pago conserva
/// sus movimientos (AC-3) y un movimiento no puede apuntar al pago de otro propietario.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RecurringPaymentLinkTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public RecurringPaymentLinkTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task RecordPayment_SavesTheExpenseAndTheNewDateTogether()
    {
        var (subscription, bank, food) = await SeedAsync(OwnerA);

        using (var scope = _services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            var tracked = (await repository.GetByIdAsync(OwnerA, subscription.Id, default))!;
            tracked.AdvanceBillingDate(Clock);
            await repository.RecordPaymentAsync(tracked, Expense(OwnerA, bank.Id, food.Id, subscription.Id), default);
        }

        await using var dbContext = _database.CreateDbContext();
        Assert.Equal(new DateOnly(2026, 11, 20), (await dbContext.Subscriptions.SingleAsync()).NextBillingDate);
        Assert.Equal(subscription.Id, (await dbContext.Transactions.SingleAsync()).RecurringPaymentId);
    }

    [Fact]
    public async Task RecordPayment_WhenTheExpenseFails_LeavesTheDateUnchanged()
    {
        // AC-2: la cuenta no existe → la FK rechaza el gasto y la fecha nueva tampoco se guarda.
        var (subscription, _, food) = await SeedAsync(OwnerA);

        using (var scope = _services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            var tracked = (await repository.GetByIdAsync(OwnerA, subscription.Id, default))!;
            tracked.AdvanceBillingDate(Clock);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repository.RecordPaymentAsync(tracked, Expense(OwnerA, Guid.NewGuid(), food.Id, subscription.Id), default));
        }

        await using var dbContext = _database.CreateDbContext();
        Assert.Equal(new DateOnly(2026, 10, 20), (await dbContext.Subscriptions.SingleAsync()).NextBillingDate);
        Assert.Empty(await dbContext.Transactions.ToListAsync());
    }

    [Fact]
    public async Task DeletePayment_KeepsItsTransactionsAndClearsTheLink()
    {
        // AC-3.
        var (subscription, bank, food) = await SeedAsync(OwnerA);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.Transactions.Add(Expense(OwnerA, bank.Id, food.Id, subscription.Id));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = _services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>().DeleteAsync(OwnerA, subscription.Id, default));
        }

        await using var check = _database.CreateDbContext();
        var kept = await check.Transactions.SingleAsync();
        Assert.Null(kept.RecurringPaymentId);
        Assert.Equal(38900m, kept.Amount);
    }

    [Fact]
    public async Task Transaction_LinkedToAnotherOwnersPayment_IsRejectedByCompositeForeignKey()
    {
        var (foreignPayment, _, _) = await SeedAsync(OwnerB);
        var (_, bank, food) = await SeedAsync(OwnerA);

        await using var dbContext = _database.CreateDbContext();
        dbContext.Transactions.Add(Expense(OwnerA, bank.Id, food.Id, foreignPayment.Id));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("fk_transactions_subscriptions_owner_id_recurring_payment_id", postgres.ConstraintName);
    }

    private async Task<(Subscription Subscription, Account Bank, Category Food)> SeedAsync(string owner)
    {
        var subscription = Subscription.Create(owner, "Netflix", null, 38900m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 20), Clock);
        var bank = Account.Create(owner, "Bank", AccountType.Bank, "COP", 100000m, Clock);
        var food = Category.Create(owner, "Subscriptions", CategoryKind.Expense, "orange", "subscriptions", Clock);
        await using var dbContext = _database.CreateDbContext();
        dbContext.AddRange(subscription, bank, food);
        await dbContext.SaveChangesAsync();
        return (subscription, bank, food);
    }

    private static Transaction Expense(string owner, Guid accountId, Guid categoryId, Guid paymentId) =>
        Transaction.Create(owner, TransactionType.Expense, accountId, null, categoryId, 38900m, new DateOnly(2026, 10, 20),
            "Netflix", null, Clock, paymentId);
}
