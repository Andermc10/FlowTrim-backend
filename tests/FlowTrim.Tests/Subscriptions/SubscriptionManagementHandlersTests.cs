using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

public sealed class SubscriptionManagementHandlersTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);

    private static TimeProvider Clock() => new FixedTimeProvider(FixedNow);

    private static Subscription NewSubscription(string name, DateOnly nextBillingDate) =>
        Subscription.Create(name, null, 10m, "COP", BillingCycle.Monthly, nextBillingDate, new FixedTimeProvider(FixedNow));

    // ----- List -----

    [Fact]
    public async Task List_ReturnsItemsOrderedByNextBillingDateThenName()
    {
        var repo = new InMemorySubscriptionRepository();
        repo.Items.Add(NewSubscription("Spotify", new DateOnly(2026, 12, 1)));
        repo.Items.Add(NewSubscription("Netflix", new DateOnly(2026, 11, 1)));
        repo.Items.Add(NewSubscription("Disney", new DateOnly(2026, 11, 1)));
        var handler = new ListSubscriptionsHandler(repo, new TestCurrentUser());

        var result = await handler.HandleAsync(1, 20, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Disney", "Netflix", "Spotify"], result.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task List_ClampsPageAndPageSize()
    {
        var repo = new InMemorySubscriptionRepository();
        var handler = new ListSubscriptionsHandler(repo, new TestCurrentUser());

        var result = await handler.HandleAsync(0, 500, CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(ListSubscriptionsHandler.MaxPageSize, result.PageSize);
    }

    // ----- Get -----

    [Fact]
    public async Task Get_ExistingId_ReturnsResponse()
    {
        var repo = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Netflix", new DateOnly(2026, 11, 1));
        repo.Items.Add(subscription);
        var handler = new GetSubscriptionHandler(repo, new TestCurrentUser());

        var result = await handler.HandleAsync(subscription.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Netflix", result!.Name);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        var handler = new GetSubscriptionHandler(new InMemorySubscriptionRepository(), new TestCurrentUser());

        var result = await handler.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    // ----- Update -----

    private static UpdateSubscriptionRequest ValidUpdate(
        string? name = "Netflix Premium",
        decimal? amount = 44900m,
        string? billingCycle = "Yearly",
        string? status = "Active") =>
        new(name, "Streaming", amount, "COP", billingCycle, new DateOnly(2026, 12, 1), status);

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var handler = new UpdateSubscriptionHandler(new InMemorySubscriptionRepository(), Clock(), new TestCurrentUser());

        var result = await handler.HandleAsync(Guid.NewGuid(), ValidUpdate(), CancellationToken.None);

        Assert.Equal(UpdateSubscriptionResult.ResultKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task Update_ValidData_UpdatesAndReturnsSuccess()
    {
        var repo = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Netflix", new DateOnly(2026, 11, 1));
        repo.Items.Add(subscription);
        var later = FixedNow.AddDays(1);
        var handler = new UpdateSubscriptionHandler(repo, new FixedTimeProvider(later), new TestCurrentUser());

        var result = await handler.HandleAsync(subscription.Id, ValidUpdate(), CancellationToken.None);

        Assert.Equal(UpdateSubscriptionResult.ResultKind.Success, result.Kind);
        Assert.Equal("Netflix Premium", result.Value!.Name);
        Assert.Equal("Yearly", result.Value.BillingCycle);
        Assert.Equal(later, result.Value.UpdatedAt);
        Assert.Equal("Netflix Premium", repo.Items[0].Name);
    }

    [Fact]
    public async Task Update_InvalidAmount_ThrowsDomainValidation()
    {
        var repo = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Netflix", new DateOnly(2026, 11, 1));
        repo.Items.Add(subscription);
        var handler = new UpdateSubscriptionHandler(repo, Clock(), new TestCurrentUser());

        // El importe ≤ 0 es una invariante de dominio: lanza y el manejador global la traduce a 400.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(subscription.Id, ValidUpdate(amount: 0m), CancellationToken.None));

        Assert.Equal("amount", ex.PropertyName);
    }

    [Fact]
    public async Task Update_InvalidStatus_ReturnsInvalid()
    {
        var repo = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Netflix", new DateOnly(2026, 11, 1));
        repo.Items.Add(subscription);
        var handler = new UpdateSubscriptionHandler(repo, Clock(), new TestCurrentUser());

        var result = await handler.HandleAsync(subscription.Id, ValidUpdate(status: "Paused"), CancellationToken.None);

        Assert.Equal(UpdateSubscriptionResult.ResultKind.Invalid, result.Kind);
        Assert.True(result.Errors.ContainsKey("status"));
    }

    // ----- Delete -----

    [Fact]
    public async Task Delete_ExistingId_ReturnsTrueAndRemoves()
    {
        var repo = new InMemorySubscriptionRepository();
        var subscription = NewSubscription("Netflix", new DateOnly(2026, 11, 1));
        repo.Items.Add(subscription);
        var handler = new DeleteSubscriptionHandler(repo, new TestCurrentUser());

        var deleted = await handler.HandleAsync(subscription.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsFalse()
    {
        var handler = new DeleteSubscriptionHandler(new InMemorySubscriptionRepository(), new TestCurrentUser());

        var deleted = await handler.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(deleted);
    }
}
