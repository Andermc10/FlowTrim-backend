using FlowTrim.Application.Usage;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Usage;

public sealed class CostPerUseTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);

    private static Subscription NewSubscription(decimal amount = 10m) =>
        Subscription.Create(
            "Netflix",
            null,
            amount,
            "COP",
            BillingCycle.Monthly,
            new DateOnly(2026, 11, 1),
            new FixedTimeProvider(FixedNow));

    [Fact]
    public void Calculate_WithUses_DividesPeriodCostByUses()
    {
        var subscription = NewSubscription();
        var records = new[] { UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 4, null) };

        var result = CostPerUseCalculator.Calculate(subscription, records, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.True(result.Calculable);
        Assert.Equal(10m, result.TotalPaid);
        Assert.Equal(4, result.TotalUses);
        Assert.Equal(2.50m, result.CostPerUse);
    }

    [Fact]
    public void Calculate_ZeroUses_IsNotCalculable()
    {
        var subscription = NewSubscription();
        var records = new[] { UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 0, null) };

        var result = CostPerUseCalculator.Calculate(subscription, records, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.False(result.Calculable);
        Assert.Null(result.CostPerUse);
        Assert.Equal("No usage in the selected period.", result.Reason);
    }

    [Fact]
    public void Calculate_NoRecords_IsNotCalculable()
    {
        var subscription = NewSubscription();

        var result = CostPerUseCalculator.Calculate(
            subscription,
            [],
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        Assert.False(result.Calculable);
        Assert.Equal(1, result.MonthsInPeriod);
        Assert.Equal(10m, result.TotalPaid);
        Assert.Null(result.CostPerUse);
    }

    [Fact]
    public void Calculate_MultipleMonths_ScalesPeriodCost()
    {
        var subscription = NewSubscription();
        var records = new[] { UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 4, null) };

        var result = CostPerUseCalculator.Calculate(subscription, records, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30));

        Assert.Equal(2, result.MonthsInPeriod);
        Assert.Equal(20m, result.TotalPaid);
        Assert.Equal(5m, result.CostPerUse);
    }

    [Fact]
    public void Calculate_OmittedBounds_DerivesPeriodFromRecords()
    {
        var subscription = NewSubscription();
        var records = new[]
        {
            UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 1, null),
            UsageRecord.Create(subscription.Id, new DateOnly(2026, 11, 10), 1, null),
        };

        var result = CostPerUseCalculator.Calculate(subscription, records, null, null);

        Assert.Equal(new DateOnly(2026, 10, 10), result.From);
        Assert.Equal(new DateOnly(2026, 11, 10), result.To);
        Assert.Equal(2, result.MonthsInPeriod);
        Assert.Equal(10m, result.CostPerUse);
    }

    [Fact]
    public void Calculate_RoundsToTwoDecimals()
    {
        var subscription = NewSubscription();
        var records = new[] { UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 3, null) };

        var result = CostPerUseCalculator.Calculate(subscription, records, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(3.33m, result.CostPerUse);
    }

    [Fact]
    public async Task GetCostPerUse_UnknownSubscription_ReturnsNull()
    {
        var handler = new GetCostPerUseHandler(new InMemorySubscriptionRepository(), new InMemoryUsageRecordRepository());

        var result = await handler.HandleAsync(Guid.NewGuid(), null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCostPerUse_ComputesForPeriod()
    {
        var subscriptions = new InMemorySubscriptionRepository();
        var usage = new InMemoryUsageRecordRepository();
        var subscription = NewSubscription();
        subscriptions.Items.Add(subscription);
        usage.Items.Add(UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 2, null));
        var handler = new GetCostPerUseHandler(subscriptions, usage);

        var result = await handler.HandleAsync(
            subscription.Id,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Calculable);
        Assert.Equal(5m, result.CostPerUse);
    }
}
