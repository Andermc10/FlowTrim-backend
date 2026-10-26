using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

/// <summary>HU-57: marcar como pagado avanza la próxima fecha exactamente un ciclo y no registra ningún pago.</summary>
public sealed class MarkSubscriptionPaidTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PaidAt = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    private static Subscription Make(BillingCycle cycle, DateOnly next, string owner = TestCurrentUser.DefaultSubject) =>
        Subscription.Create(owner, "Netflix", null, 38900m, "COP", cycle, next, new FixedTimeProvider(CreatedAt));

    [Theory]
    [InlineData(BillingCycle.Monthly, "2026-10-20", "2026-11-20")]
    [InlineData(BillingCycle.Yearly, "2026-10-20", "2027-10-20")]
    [InlineData(BillingCycle.Monthly, "2026-01-31", "2026-02-28")]
    [InlineData(BillingCycle.Monthly, "2026-12-15", "2027-01-15")]
    [InlineData(BillingCycle.Yearly, "2028-02-29", "2029-02-28")]
    public void AdvanceBillingDate_MovesExactlyOneCycle(BillingCycle cycle, string current, string expected)
    {
        var subscription = Make(cycle, DateOnly.Parse(current));

        subscription.AdvanceBillingDate(new FixedTimeProvider(PaidAt));

        Assert.Equal(DateOnly.Parse(expected), subscription.NextBillingDate);
        Assert.Equal(PaidAt, subscription.UpdatedAt);
        Assert.Equal(CreatedAt, subscription.CreatedAt);
    }

    [Fact]
    public void AdvanceBillingDate_Inactive_Throws()
    {
        var subscription = Make(BillingCycle.Monthly, new DateOnly(2026, 10, 20));
        subscription.Update(subscription.Name, null, subscription.Amount, subscription.Currency, subscription.BillingCycle,
            subscription.NextBillingDate, SubscriptionStatus.Inactive, new FixedTimeProvider(CreatedAt));

        var exception = Assert.Throws<DomainValidationException>(() => subscription.AdvanceBillingDate(new FixedTimeProvider(PaidAt)));

        Assert.Equal("status", exception.PropertyName);
        Assert.Equal(new DateOnly(2026, 10, 20), subscription.NextBillingDate);
    }

    [Fact]
    public async Task Handle_OwnSubscription_PersistsTheNextDate()
    {
        var repository = new InMemorySubscriptionRepository();
        var subscription = Make(BillingCycle.Monthly, new DateOnly(2026, 10, 18));
        repository.Items.Add(subscription);
        var handler = new MarkSubscriptionPaidHandler(
            repository, new InMemoryAccountRepository(), new InMemoryCategoryRepository(), new FixedTimeProvider(PaidAt), new TestCurrentUser());

        var result = await handler.HandleAsync(subscription.Id, null, CancellationToken.None);

        Assert.Equal(HandlerResultKind.Success, result.Kind);
        Assert.Equal(new DateOnly(2026, 11, 18), result.Value!.Subscription.NextBillingDate);
        Assert.Equal(1, repository.SaveCount);
        Assert.Null(result.Value.Transaction);
        Assert.Empty(repository.Payments);
    }

    [Fact]
    public async Task Handle_OtherOwnerSubscription_ReturnsNotFoundAndChangesNothing()
    {
        var repository = new InMemorySubscriptionRepository();
        var foreign = Make(BillingCycle.Monthly, new DateOnly(2026, 10, 18), owner: "auth0|someone-else");
        repository.Items.Add(foreign);
        var handler = new MarkSubscriptionPaidHandler(
            repository, new InMemoryAccountRepository(), new InMemoryCategoryRepository(), new FixedTimeProvider(PaidAt), new TestCurrentUser());

        var result = await handler.HandleAsync(foreign.Id, null, CancellationToken.None);

        Assert.Equal(HandlerResultKind.NotFound, result.Kind);
        Assert.Equal(new DateOnly(2026, 10, 18), foreign.NextBillingDate);
        Assert.Equal(0, repository.SaveCount);
    }
}
