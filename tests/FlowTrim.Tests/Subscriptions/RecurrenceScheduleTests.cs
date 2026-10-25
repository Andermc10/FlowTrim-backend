using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

/// <summary>Ciclos, equivalentes mensuales y proyección de cobros de EP-14 (HU-72, D3/D4, §5).</summary>
public sealed class RecurrenceScheduleTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(BillingCycle.Weekly, "2026-10-06", "2026-10-13")]
    [InlineData(BillingCycle.Biweekly, "2026-12-25", "2027-01-08")]
    [InlineData(BillingCycle.Monthly, "2026-01-31", "2026-02-28")]
    [InlineData(BillingCycle.Quarterly, "2026-11-30", "2027-02-28")]
    [InlineData(BillingCycle.Yearly, "2028-02-29", "2029-02-28")]
    public void Next_EachCycle_StepsOneCycleWithEndOfMonthClamp(BillingCycle cycle, string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), RecurrenceSchedule.Next(DateOnly.Parse(date), cycle));
    }

    [Theory]
    [InlineData(BillingCycle.Weekly, 10000, 43333.33)]
    [InlineData(BillingCycle.Biweekly, 10000, 21666.67)]
    [InlineData(BillingCycle.Monthly, 10000, 10000)]
    [InlineData(BillingCycle.Quarterly, 100, 33.33)]
    [InlineData(BillingCycle.Yearly, 120000, 10000)]
    public void MonthlyEquivalent_EachCycle_UsesTheSpecFormula(BillingCycle cycle, double amount, double expected)
    {
        Assert.Equal((decimal)expected, RecurrenceSchedule.MonthlyEquivalent((decimal)amount, cycle));
    }

    [Fact]
    public void Occurrences_WeeklyInA31DayMonth_ReturnsEveryWeek()
    {
        // AC-1 de HU-72: próximo cobro el 6 de un mes de 31 días → 6, 13, 20 y 27.
        var dates = RecurrenceSchedule.Occurrences(
            new DateOnly(2026, 10, 6), BillingCycle.Weekly, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(
            [new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 27)],
            dates);
    }

    [Fact]
    public void Occurrences_BeforeFrom_AreSkippedButKeepTheStep()
    {
        var dates = RecurrenceSchedule.Occurrences(
            new DateOnly(2026, 9, 1), BillingCycle.Biweekly, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal([new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 27)], dates);
    }

    [Fact]
    public void Occurrences_NextDateAfterTo_IsEmpty()
    {
        Assert.Empty(RecurrenceSchedule.Occurrences(
            new DateOnly(2026, 11, 1), BillingCycle.Weekly, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Occurrences_MonthlyFromThe31st_KeepsTheClampedDay()
    {
        var dates = RecurrenceSchedule.Occurrences(
            new DateOnly(2026, 1, 31), BillingCycle.Monthly, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 28), new DateOnly(2026, 4, 28)],
            dates);
    }

    [Theory]
    [InlineData(BillingCycle.Weekly, "2026-10-08")]
    [InlineData(BillingCycle.Biweekly, "2026-10-15")]
    [InlineData(BillingCycle.Quarterly, "2027-01-01")]
    public void AdvanceBillingDate_NewCycles_StepsOneCycle(BillingCycle cycle, string expected)
    {
        var subscription = Subscription.Create("owner", "Rent", null, 100m, "COP", cycle, new DateOnly(2026, 10, 1), Clock);

        subscription.AdvanceBillingDate(Clock);

        Assert.Equal(DateOnly.Parse(expected), subscription.NextBillingDate);
    }

    [Fact]
    public void Create_WithoutDetails_IsASubscriptionWithoutLinkOrDefaults()
    {
        var subscription = Subscription.Create("owner", "Netflix", null, 100m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 1), Clock);

        Assert.Equal(SubscriptionKind.Subscription, subscription.Kind);
        Assert.Null(subscription.PaymentUrl);
        Assert.Null(subscription.DefaultAccountId);
        Assert.Null(subscription.CategoryId);
    }

    [Theory]
    [InlineData("https://pay.example.com/bills?id=1")]
    [InlineData("  https://example.com  ")]
    public void Create_WithHttpsLink_KeepsTheTrimmedLink(string url)
    {
        var subscription = Create(new RecurringPaymentDetails(SubscriptionKind.Bill, url, null, null));

        Assert.Equal(url.Trim(), subscription.PaymentUrl);
        Assert.Equal(SubscriptionKind.Bill, subscription.Kind);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com/pay")]
    public void Create_WithUnsafeLink_FailsOnPaymentUrl(string url)
    {
        // AC-2 de HU-72.
        var exception = Assert.Throws<DomainValidationException>(
            () => Create(new RecurringPaymentDetails(SubscriptionKind.Bill, url, null, null)));

        Assert.Equal("paymentUrl", exception.PropertyName);
    }

    [Fact]
    public void Create_WithTooLongLink_FailsOnPaymentUrl()
    {
        var url = "https://example.com/" + new string('a', Subscription.PaymentUrlMaxLength);

        var exception = Assert.Throws<DomainValidationException>(
            () => Create(new RecurringPaymentDetails(SubscriptionKind.Bill, url, null, null)));

        Assert.Equal("paymentUrl", exception.PropertyName);
    }

    [Fact]
    public void Update_WithoutDetails_KeepsKindLinkAndDefaults()
    {
        var account = Guid.NewGuid();
        var subscription = Create(new RecurringPaymentDetails(SubscriptionKind.Bill, "https://example.com", account, null));

        subscription.Update("Rent", null, 200m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 1), SubscriptionStatus.Active, Clock);

        Assert.Equal(SubscriptionKind.Bill, subscription.Kind);
        Assert.Equal("https://example.com", subscription.PaymentUrl);
        Assert.Equal(account, subscription.DefaultAccountId);
    }

    private static Subscription Create(RecurringPaymentDetails details) =>
        Subscription.Create("owner", "Rent", null, 100m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 1), Clock, details);
}
