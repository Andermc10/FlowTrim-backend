using FlowTrim.Application.Summaries;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Summaries;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Summaries;

public sealed class SummaryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly SampleDate = new(2026, 11, 1);

    private static TimeProvider Clock() => new FixedTimeProvider(FixedNow);

    private static Subscription Make(decimal amount, BillingCycle cycle, string currency = "COP", bool active = true)
    {
        var subscription = Subscription.Create("Service", null, amount, currency, cycle, SampleDate, Clock());
        if (!active)
        {
            subscription.Update("Service", null, amount, currency, cycle, SampleDate, SubscriptionStatus.Inactive, Clock());
        }

        return subscription;
    }

    // ----- GetMonthlyEquivalent -----

    [Fact]
    public void GetMonthlyEquivalent_Monthly_ReturnsAmount()
    {
        var subscription = Make(38900m, BillingCycle.Monthly);
        Assert.Equal(38900m, subscription.GetMonthlyEquivalent());
    }

    [Fact]
    public void GetMonthlyEquivalent_Yearly_DividesBy12AndRoundsToTwoDecimals()
    {
        var subscription = Make(100000m, BillingCycle.Yearly);
        Assert.Equal(8333.33m, subscription.GetMonthlyEquivalent());
    }

    // ----- SummaryCalculator -----

    [Fact]
    public void Calculate_SumsMonthlyAndProjectsAnnualPerCurrency()
    {
        var subscriptions = new[]
        {
            Make(38900m, BillingCycle.Monthly),   // 38900
            Make(120000m, BillingCycle.Yearly),   // 10000
        };

        var result = SummaryCalculator.Calculate(subscriptions);

        var cop = Assert.Single(result);
        Assert.Equal("COP", cop.Currency);
        Assert.Equal(48900m, cop.MonthlyEquivalent);
        Assert.Equal(586800m, cop.AnnualProjection);
        Assert.Equal(2, cop.ActiveCount);
    }

    [Fact]
    public void Calculate_ExcludesInactive()
    {
        var subscriptions = new[]
        {
            Make(10000m, BillingCycle.Monthly, active: true),
            Make(99999m, BillingCycle.Monthly, active: false),
        };

        var result = SummaryCalculator.Calculate(subscriptions);

        var cop = Assert.Single(result);
        Assert.Equal(10000m, cop.MonthlyEquivalent);
        Assert.Equal(1, cop.ActiveCount);
    }

    [Fact]
    public void Calculate_GroupsByCurrency_WithoutSummingAcross()
    {
        var subscriptions = new[]
        {
            Make(38900m, BillingCycle.Monthly, "COP"),
            Make(10m, BillingCycle.Monthly, "USD"),
        };

        var result = SummaryCalculator.Calculate(subscriptions);

        Assert.Equal(2, result.Count);
        Assert.Equal("COP", result[0].Currency); // ordenado por código
        Assert.Equal("USD", result[1].Currency);
        Assert.Equal(38900m, result[0].MonthlyEquivalent);
        Assert.Equal(10m, result[1].MonthlyEquivalent);
    }

    [Fact]
    public void Calculate_NoActive_ReturnsEmpty()
    {
        var subscriptions = new[] { Make(10000m, BillingCycle.Monthly, active: false) };

        var result = SummaryCalculator.Calculate(subscriptions);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_YearlyNotDivisibleByTwelve_RoundsPerItemThenProjects()
    {
        // 100000 / 12 = 8333.333… → 8333.33 (redondeo por ítem); anual = 8333.33 × 12 = 99999.96.
        var subscriptions = new[] { Make(100000m, BillingCycle.Yearly) };

        var result = SummaryCalculator.Calculate(subscriptions);

        var cop = Assert.Single(result);
        Assert.Equal(8333.33m, cop.MonthlyEquivalent);
        Assert.Equal(99999.96m, cop.AnnualProjection);
    }

    [Fact]
    public void Calculate_CurrencyWithOnlyInactive_IsNotListed()
    {
        var subscriptions = new[]
        {
            Make(10000m, BillingCycle.Monthly, "COP", active: true),
            Make(20m, BillingCycle.Monthly, "USD", active: false),
        };

        var result = SummaryCalculator.Calculate(subscriptions);

        var only = Assert.Single(result);
        Assert.Equal("COP", only.Currency);
    }

    // ----- GetSummaryHandler -----

    [Fact]
    public async Task GetSummary_ReturnsPerCurrencyTotalsForActiveSubscriptions()
    {
        var repo = new InMemorySubscriptionRepository();
        repo.Items.Add(Make(38900m, BillingCycle.Monthly, "COP"));
        repo.Items.Add(Make(120000m, BillingCycle.Yearly, "COP"));
        repo.Items.Add(Make(9.99m, BillingCycle.Monthly, "USD"));
        repo.Items.Add(Make(50000m, BillingCycle.Monthly, "COP", active: false));
        var handler = new GetSummaryHandler(repo);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, result.Currencies.Count);
        var cop = result.Currencies.Single(c => c.Currency == "COP");
        Assert.Equal(48900m, cop.MonthlyEquivalent);
        Assert.Equal(586800m, cop.AnnualProjection);
        Assert.Equal(2, cop.ActiveCount);
        var usd = result.Currencies.Single(c => c.Currency == "USD");
        Assert.Equal(9.99m, usd.MonthlyEquivalent);
    }

    [Fact]
    public async Task GetSummary_NoSubscriptions_ReturnsEmptyCurrencies()
    {
        var handler = new GetSummaryHandler(new InMemorySubscriptionRepository());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Empty(result.Currencies);
    }
}
