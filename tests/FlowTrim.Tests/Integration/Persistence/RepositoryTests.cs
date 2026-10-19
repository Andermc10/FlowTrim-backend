using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Usage;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>Repositorios reales (SQL generado por EF Core) contra PostgreSQL (HU-47, AC-4/AC-5).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 30, 45, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public RepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task SubscriptionRepository_TwoOwners_ListsCountsAndGetsOnlyOwnerRows()
    {
        var a = await AddAsync(OwnerA, "Netflix", new DateOnly(2026, 11, 1));
        var b = await AddAsync(OwnerB, "Disney", new DateOnly(2026, 11, 1));

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();

        Assert.Equal(["Netflix"], (await repository.ListAsync(OwnerA, 0, 20, default)).Select(s => s.Name));
        Assert.Equal(1, await repository.CountAsync(OwnerA, default));
        Assert.Equal(["Netflix"], (await repository.ListActiveAsync(OwnerA, default)).Select(s => s.Name));
        Assert.NotNull(await repository.GetByIdAsync(OwnerA, a.Id, default));
        Assert.Null(await repository.GetByIdAsync(OwnerA, b.Id, default));
        Assert.False(await repository.DeleteAsync(OwnerA, b.Id, default));
        Assert.NotNull(await repository.GetByIdAsync(OwnerB, b.Id, default));
    }

    [Fact]
    public async Task SubscriptionRepository_List_OrdersByNextBillingDateThenNameAndPages()
    {
        await AddAsync(OwnerA, "Spotify", new DateOnly(2026, 12, 1));
        await AddAsync(OwnerA, "Netflix", new DateOnly(2026, 11, 1));
        await AddAsync(OwnerA, "Disney", new DateOnly(2026, 11, 1));

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();

        var firstPage = await repository.ListAsync(OwnerA, 0, 2, default);
        var secondPage = await repository.ListAsync(OwnerA, 2, 2, default);

        Assert.Equal(["Disney", "Netflix"], firstPage.Select(s => s.Name));
        Assert.Equal(["Spotify"], secondPage.Select(s => s.Name));
    }

    [Fact]
    public async Task SubscriptionRepository_ListAll_ReturnsActiveAndInactiveOfOwnerOrderedByName()
    {
        var inactive = await AddAsync(OwnerA, "Zeta", new DateOnly(2026, 11, 1));
        await AddAsync(OwnerA, "Alpha", new DateOnly(2026, 12, 1));
        await AddAsync(OwnerB, "Other", new DateOnly(2026, 11, 1));
        await using (var scope = _services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
            inactive.Update("Zeta", null, 38900m, "COP", BillingCycle.Monthly, new DateOnly(2026, 11, 1),
                SubscriptionStatus.Inactive, new FixedTimeProvider(FixedNow));
            await repository.UpdateAsync(inactive, default);
        }

        await using var readScope = _services.CreateAsyncScope();
        var all = await readScope.ServiceProvider.GetRequiredService<ISubscriptionRepository>().ListAllAsync(OwnerA, default);

        Assert.Equal(["Alpha", "Zeta"], all.Select(s => s.Name));
        Assert.Equal(SubscriptionStatus.Inactive, all[1].Status);
    }

    [Fact]
    public async Task UsageRecordRepository_OtherOwner_ReturnsEmpty()
    {
        var subscription = await AddAsync(OwnerA, "Netflix", new DateOnly(2026, 11, 1));
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 10, 10), 3);

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUsageRecordRepository>();

        Assert.Single(await repository.ListBySubscriptionAsync(OwnerA, subscription.Id, null, null, default));
        Assert.Empty(await repository.ListBySubscriptionAsync(OwnerB, subscription.Id, null, null, default));
    }

    [Fact]
    public async Task UsageRecordRepository_DateRange_IsInclusiveAndOrderedDescending()
    {
        var subscription = await AddAsync(OwnerA, "Netflix", new DateOnly(2026, 11, 1));
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 9, 30), 1);
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 10, 1), 2);
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 10, 15), 3);
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 10, 31), 4);
        await AddUsageAsync(subscription.Id, new DateOnly(2026, 11, 1), 5);

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUsageRecordRepository>();

        var october = await repository.ListBySubscriptionAsync(
            OwnerA, subscription.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);

        Assert.Equal([4, 3, 2], october.Select(u => u.UsageCount));
    }

    [Fact]
    public async Task Subscription_RoundTrip_PreservesDecimalDateAndUtcTimestamps()
    {
        // 10.005 lo redondea el dominio; se verifica lo que PostgreSQL devuelve, no lo que se envió.
        var stored = await AddAsync(OwnerA, "Spotify", new DateOnly(2026, 2, 28), amount: 10.005m, currency: "usd");

        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var reloaded = await repository.GetByIdAsync(OwnerA, stored.Id, default);

        Assert.NotNull(reloaded);
        Assert.Equal(stored.Amount, reloaded.Amount);
        Assert.Equal(2, reloaded.Amount.Scale);
        Assert.Equal("USD", reloaded.Currency);
        Assert.Equal(new DateOnly(2026, 2, 28), reloaded.NextBillingDate);
        Assert.Equal(BillingCycle.Monthly, reloaded.BillingCycle);
        Assert.Equal(SubscriptionStatus.Active, reloaded.Status);
        Assert.Equal(FixedNow, reloaded.CreatedAt);
        Assert.Equal(TimeSpan.Zero, reloaded.CreatedAt.Offset);
    }

    private async Task<Subscription> AddAsync(
        string ownerId, string name, DateOnly nextBillingDate, decimal amount = 38900m, string currency = "COP")
    {
        var subscription = Subscription.Create(
            ownerId, name, null, amount, currency, BillingCycle.Monthly, nextBillingDate, new FixedTimeProvider(FixedNow));
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>().AddAsync(subscription, default);
        return subscription;
    }

    private async Task AddUsageAsync(Guid subscriptionId, DateOnly date, int count)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUsageRecordRepository>()
            .AddAsync(UsageRecord.Create(subscriptionId, date, count, null), default);
    }
}
