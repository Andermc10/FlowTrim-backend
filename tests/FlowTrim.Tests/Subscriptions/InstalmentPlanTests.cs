using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

/// <summary>Planes de cuotas (EP-14 HU-74, D8): validación, conteo, finalización y proyección limitada.</summary>
public sealed class InstalmentPlanTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_Plan_StartsWithTheGivenPaidInstalments()
    {
        var plan = Plan(total: 12, paid: 3);

        Assert.Equal(12, plan.TotalInstalments);
        Assert.Equal(3, plan.PaidInstalments);
        Assert.Equal(9, plan.RemainingInstalments);
        Assert.False(plan.IsCompleted);
    }

    [Theory]
    [InlineData(null, 0, "totalInstalments")]
    [InlineData(0, 0, "totalInstalments")]
    [InlineData(601, 0, "totalInstalments")]
    [InlineData(3, 3, "paidInstalments")]
    [InlineData(3, -1, "paidInstalments")]
    public void Create_InvalidCounts_FailOnTheField(int? total, int paid, string field)
    {
        var exception = Assert.Throws<DomainValidationException>(() => Plan(total, paid));

        Assert.Equal(field, exception.PropertyName);
    }

    [Fact]
    public void Create_OtherKinds_IgnoreInstalmentCounts()
    {
        var bill = Subscription.Create("owner", "Rent", null, 100m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 1), Clock,
            new RecurringPaymentDetails(SubscriptionKind.Bill, null, null, null, 12, 4));

        Assert.Null(bill.TotalInstalments);
        Assert.Equal(0, bill.PaidInstalments);
        Assert.Null(bill.RemainingInstalments);
    }

    [Fact]
    public void AdvanceBillingDate_LastInstalment_CompletesThePlan()
    {
        // AC-1: 3 cuotas con 2 pagadas → queda 1; pagarla completa el plan y sus cobros esperados caen a cero.
        var plan = Plan(total: 3, paid: 2);
        var monthEnd = new DateOnly(2026, 10, 31);
        Assert.Single(DailyAllowanceCalculator.ChargesUntil(plan, new DateOnly(2027, 12, 31)));

        plan.AdvanceBillingDate(Clock);

        Assert.Equal(3, plan.PaidInstalments);
        Assert.True(plan.IsCompleted);
        Assert.Equal(SubscriptionStatus.Inactive, plan.Status);
        var cop = DailyAllowanceCalculator.Calculate(new DateOnly(2026, 10, 1), [new AccountBalance(AccountType.Bank, "COP", 10m, false)], [], [], [plan]);
        Assert.Equal(0m, Assert.Single(cop).CommittedUntilMonthEnd);
        Assert.Empty(plan.GetOccurrences(plan.NextBillingDate, monthEnd));
    }

    [Fact]
    public void AdvanceBillingDate_CompletedPlan_IsRejected()
    {
        var plan = Plan(total: 1, paid: 0);
        plan.AdvanceBillingDate(Clock);

        var exception = Assert.Throws<DomainValidationException>(() => plan.AdvanceBillingDate(Clock));

        Assert.Equal("status", exception.PropertyName);
    }

    [Fact]
    public void Occurrences_WeeklyPlan_NeverProjectsBeyondTheRemainingInstalments()
    {
        var plan = Subscription.Create("owner", "Phone", null, 50000m, "COP", BillingCycle.Weekly, new DateOnly(2026, 10, 6), Clock,
            new RecurringPaymentDetails(SubscriptionKind.Instalment, null, null, null, 5, 3));

        Assert.Equal(
            [new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 13)],
            plan.GetOccurrences(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Update_CompletedPlan_CanOnlyBeReactivatedWithMoreInstalments()
    {
        var plan = Plan(total: 1, paid: 0);
        plan.AdvanceBillingDate(Clock);

        var stillCompleted = Assert.Throws<DomainValidationException>(() => Reactivate(plan, total: 1));
        Reactivate(plan, total: 4);

        Assert.Equal("status", stillCompleted.PropertyName);
        Assert.Equal(SubscriptionStatus.Active, plan.Status);
        Assert.Equal(1, plan.PaidInstalments);
        Assert.Equal(3, plan.RemainingInstalments);
    }

    [Fact]
    public void Update_TotalBelowPaid_FailsOnTotal()
    {
        var plan = Plan(total: 12, paid: 5);

        var exception = Assert.Throws<DomainValidationException>(() => Reactivate(plan, total: 4));

        Assert.Equal("totalInstalments", exception.PropertyName);
        Assert.Equal(12, plan.TotalInstalments);
    }

    private static Subscription Plan(int? total, int paid) =>
        Subscription.Create("owner", "Laptop", null, 250000m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 15), Clock,
            new RecurringPaymentDetails(SubscriptionKind.Instalment, null, null, null, total, paid));

    private static void Reactivate(Subscription plan, int total) =>
        plan.Update(plan.Name, null, plan.Amount, plan.Currency, plan.BillingCycle, plan.NextBillingDate, SubscriptionStatus.Active, Clock,
            new RecurringPaymentDetails(SubscriptionKind.Instalment, null, null, null, total));
}
