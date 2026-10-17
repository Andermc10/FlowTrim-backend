using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

public sealed class GetUpcomingChargesHandlerTests
{
    // 12:00 UTC → 07:00 en UTC-5, mismo día: fecha de referencia 2026-10-14.
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ReferenceDate = new(2026, 10, 14);

    private static TimeProvider Clock() => new FixedTimeProvider(FixedNow);

    private static Subscription WithDate(string name, DateOnly date, bool active = true)
    {
        var subscription = Subscription.Create(TestCurrentUser.DefaultSubject, name, null, 10m, "COP", BillingCycle.Monthly, date, Clock());
        if (!active)
        {
            subscription.Update(name, null, 10m, "COP", BillingCycle.Monthly, date, SubscriptionStatus.Inactive, Clock());
        }

        return subscription;
    }

    [Fact]
    public async Task GetUpcoming_UsesServerReferenceDate()
    {
        var handler = new GetUpcomingChargesHandler(new InMemorySubscriptionRepository(), Clock(), new TestCurrentUser());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(ReferenceDate, result.ReferenceDate);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetUpcoming_OrdersByDateThenNameAndTagsWindows()
    {
        var repo = new InMemorySubscriptionRepository();
        repo.Items.Add(WithDate("Later", ReferenceDate.AddDays(10)));     // Later
        repo.Items.Add(WithDate("Overdue", ReferenceDate.AddDays(-2)));   // Overdue
        repo.Items.Add(WithDate("Soon-B", ReferenceDate.AddDays(2)));     // DueWithin3Days
        repo.Items.Add(WithDate("Soon-A", ReferenceDate.AddDays(2)));     // DueWithin3Days (same date → by name)
        var handler = new GetUpcomingChargesHandler(repo, Clock(), new TestCurrentUser());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(["Overdue", "Soon-A", "Soon-B", "Later"], result.Items.Select(i => i.Name));
        Assert.Equal("Overdue", result.Items[0].AlertWindow);
        Assert.Equal(-2, result.Items[0].DaysUntilCharge);
        Assert.Equal("DueWithin3Days", result.Items[1].AlertWindow);
        Assert.Equal("Later", result.Items[3].AlertWindow);
    }

    [Fact]
    public async Task GetUpcoming_ExcludesInactive()
    {
        var repo = new InMemorySubscriptionRepository();
        repo.Items.Add(WithDate("Active", ReferenceDate.AddDays(1)));
        repo.Items.Add(WithDate("Inactive", ReferenceDate.AddDays(1), active: false));
        var handler = new GetUpcomingChargesHandler(repo, Clock(), new TestCurrentUser());

        var result = await handler.HandleAsync(CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Active", item.Name);
    }
}
