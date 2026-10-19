using FlowTrim.Domain.Analytics;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Analytics;

public sealed class AnalyticsCalculatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Reference = new(2026, 10, 18);

    private static Subscription Make(
        string name, decimal amount, BillingCycle cycle = BillingCycle.Monthly, string currency = "COP",
        string? category = null, DateOnly? next = null, bool active = true)
    {
        var clock = new FixedTimeProvider(FixedNow);
        var date = next ?? new DateOnly(2026, 11, 1);
        var subscription = Subscription.Create(TestCurrentUser.DefaultSubject, name, category, amount, currency, cycle, date, clock);
        if (!active)
        {
            subscription.Update(name, category, amount, currency, cycle, date, SubscriptionStatus.Inactive, clock);
        }

        return subscription;
    }

    [Fact]
    public void Calculate_SpecExample_MatchesMonthlyTotalsAndExpectedCharges()
    {
        var result = AnalyticsCalculator.Calculate(
        [
            Make("Netflix", 38900m, next: new DateOnly(2026, 10, 25)),
            Make("Cloud Storage", 120000m, BillingCycle.Yearly, next: new DateOnly(2027, 2, 15)),
            Make("Music", 9.99m, currency: "USD"),
        ], Reference, 12);

        Assert.Equal(["COP", "USD"], result.Select(c => c.Currency));
        var cop = result[0];
        Assert.Equal(48900.00m, cop.MonthlyTotal);
        Assert.Equal(9.99m, result[1].MonthlyTotal);
        Assert.Equal(12, cop.ExpectedCharges.Count);
        Assert.Equal((2026, 10), (cop.ExpectedCharges[0].Year, cop.ExpectedCharges[0].Month));
        Assert.All(cop.ExpectedCharges.Where(m => !(m.Year == 2027 && m.Month == 2)), m => Assert.Equal(38900m, m.Amount));
        var february = Assert.Single(cop.ExpectedCharges, m => m.Year == 2027 && m.Month == 2);
        Assert.Equal(158900m, february.Amount);
        Assert.Equal(2, february.Count);
    }

    [Fact]
    public void ByCategory_GroupsSortsAndComputesShare()
    {
        var cop = AnalyticsCalculator.Calculate(
        [
            Make("Netflix", 30000m, category: "Streaming"),
            Make("Disney", 20000m, category: "Streaming"),
            Make("Gym", 40000m, category: "Health"),
            Make("Notes", 25000m, category: "Apps"),
        ], Reference, 1)[0];

        Assert.Equal(["Streaming", "Health", "Apps"], cop.ByCategory.Select(c => c.Category));
        Assert.Equal(50000m, cop.ByCategory[0].MonthlyEquivalent);
        Assert.Equal(2, cop.ByCategory[0].Count);
        // 50 000 / 115 000 = 43.48 %, 40 000 / 115 000 = 34.78 %, 25 000 / 115 000 = 21.74 % (1 decimal).
        Assert.Equal([43.5m, 34.8m, 21.7m], cop.ByCategory.Select(c => c.Share));
    }

    [Fact]
    public void ByCategory_TiesAreOrderedByName_AndMissingCategoryIsUncategorized()
    {
        var cop = AnalyticsCalculator.Calculate(
        [
            Make("A", 10000m, category: "Music"),
            Make("B", 10000m),
            Make("C", 10000m, category: "Apps"),
        ], Reference, 1)[0];

        Assert.Equal(["Apps", "Music", AnalyticsCalculator.UncategorizedLabel], cop.ByCategory.Select(c => c.Category));
        Assert.Equal([33.3m, 33.3m, 33.3m], cop.ByCategory.Select(c => c.Share));
    }

    [Fact]
    public void Top_LimitsToFiveOrderedByMonthlyEquivalentThenName()
    {
        var cop = AnalyticsCalculator.Calculate(
        [
            Make("Small", 1000m), Make("Beta", 9000m), Make("Alpha", 9000m), Make("Yearly", 240000m, BillingCycle.Yearly),
            Make("Mid", 5000m), Make("Big", 50000m), Make("Tiny", 500m),
        ], Reference, 1)[0];

        Assert.Equal(["Big", "Yearly", "Alpha", "Beta", "Mid"], cop.TopSubscriptions.Select(t => t.Name));
        Assert.Equal(20000m, cop.TopSubscriptions[1].MonthlyEquivalent);
    }

    [Fact]
    public void ExpectedCharges_MonthlyStartsInItsBillingMonth()
    {
        var cop = AnalyticsCalculator.Calculate([Make("Later", 10000m, next: new DateOnly(2027, 1, 5))], Reference, 6)[0];

        Assert.Equal([0m, 0m, 0m, 10000m, 10000m, 10000m], cop.ExpectedCharges.Select(m => m.Amount));
    }

    [Fact]
    public void ExpectedCharges_FutureYearlyIsNotChargedBeforeItsFirstDate()
    {
        var cop = AnalyticsCalculator.Calculate(
            [Make("Domain", 60000m, BillingCycle.Yearly, next: new DateOnly(2027, 10, 1))], Reference, 13)[0];

        Assert.Equal(0m, cop.ExpectedCharges[0].Amount);
        Assert.Equal(60000m, cop.ExpectedCharges[12].Amount);
        Assert.Equal(60000m, cop.ExpectedCharges.Sum(m => m.Amount));
    }

    [Fact]
    public void ExpectedCharges_OverdueMonthlyStartsAtReferenceMonth_OverdueYearlyAtNextAnniversary()
    {
        var cop = AnalyticsCalculator.Calculate(
        [
            Make("Overdue monthly", 10000m, next: new DateOnly(2026, 8, 3)),
            Make("Overdue yearly", 120000m, BillingCycle.Yearly, next: new DateOnly(2026, 9, 10)),
        ], Reference, 12)[0];

        Assert.Equal(10000m, cop.ExpectedCharges[0].Amount);
        var september = Assert.Single(cop.ExpectedCharges, m => m.Amount > 10000m);
        Assert.Equal((2027, 9, 130000m), (september.Year, september.Month, september.Amount));
    }

    [Fact]
    public void Calculate_ExcludesInactive_AndReturnsEmptyWithoutActiveSubscriptions()
    {
        var result = AnalyticsCalculator.Calculate(
            [Make("Active", 10000m), Make("Paused", 99000m, active: false)], Reference, 1);

        Assert.Equal(10000m, Assert.Single(result).MonthlyTotal);
        Assert.Empty(AnalyticsCalculator.Calculate([Make("Paused", 99000m, active: false)], Reference, 1));
    }

    [Fact]
    public void Calculate_NonPositiveMonths_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalyticsCalculator.Calculate([], Reference, 0));
    }
}
