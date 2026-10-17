using FlowTrim.Application.Simulations;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Summaries;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Security;

public sealed class OwnerIsolationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly BillingDate = new(2026, 11, 1);

    [Fact]
    public async Task List_WithTwoOwners_ReturnsOnlyCurrentOwnerRows()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.Items.Add(NewSubscription("Owner A", "owner-a"));
        repository.Items.Add(NewSubscription("Owner B", "owner-b"));
        var handler = new ListSubscriptionsHandler(repository, new TestCurrentUser("owner-a"));

        var result = await handler.HandleAsync(1, 20, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Owner A", item.Name);
    }

    [Fact]
    public async Task GetUpdateAndDelete_WithOtherOwner_ReturnNotFound()
    {
        var repository = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Owner B", "owner-b");
        repository.Items.Add(subscription);
        var currentUser = new TestCurrentUser("owner-a");

        var get = new GetSubscriptionHandler(repository, currentUser);
        var update = new UpdateSubscriptionHandler(repository, new FixedTimeProvider(FixedNow), currentUser);
        var delete = new DeleteSubscriptionHandler(repository, currentUser);

        Assert.Null(await get.HandleAsync(subscription.Id, CancellationToken.None));

        var updateResult = await update.HandleAsync(
            subscription.Id,
            new UpdateSubscriptionRequest(
                "Changed",
                null,
                10m,
                "COP",
                "Monthly",
                BillingDate,
                "Active"),
            CancellationToken.None);

        Assert.Equal(UpdateSubscriptionResult.ResultKind.NotFound, updateResult.Kind);
        Assert.False(await delete.HandleAsync(subscription.Id, CancellationToken.None));
        Assert.Equal("Owner B", subscription.Name);
    }

    [Fact]
    public async Task Summary_WithTwoOwners_UsesOnlyCurrentOwnerRows()
    {
        var repository = new InMemorySubscriptionRepository();
        repository.Items.Add(NewSubscription("Owner A", "owner-a", 100m));
        repository.Items.Add(NewSubscription("Owner B", "owner-b", 900m));
        var handler = new GetSummaryHandler(repository, new TestCurrentUser("owner-a"));

        var result = await handler.HandleAsync(CancellationToken.None);

        var cop = Assert.Single(result.Currencies);
        Assert.Equal(100m, cop.MonthlyEquivalent);
    }

    [Fact]
    public async Task Simulation_WithOtherOwnerSelection_DoesNotExposeOtherOwnerData()
    {
        var repository = new InMemorySubscriptionRepository();
        var ownerB = NewSubscription("Owner B", "owner-b", 900m);
        repository.Items.Add(ownerB);
        var handler = new SimulateHandler(repository, new TestCurrentUser("owner-a"));

        var result = await handler.HandleAsync(
            new SimulationRequest([ownerB.Id], [12]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Currencies);
    }

    private static Subscription NewSubscription(string name, string ownerId, decimal amount = 10m) =>
        Subscription.CreateForOwner(
            ownerId,
            name,
            null,
            amount,
            "COP",
            BillingCycle.Monthly,
            BillingDate,
            new FixedTimeProvider(FixedNow));
}
