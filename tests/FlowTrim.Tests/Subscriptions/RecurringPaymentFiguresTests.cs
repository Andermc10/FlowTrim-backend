using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Analytics;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Simulations;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Summaries;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

/// <summary>
/// Los cálculos que consumen los cobros (Hoy, analítica, resumen y simulador) cuentan los ciclos nuevos con
/// <see cref="RecurrenceSchedule"/> y no cambian para mensual y anual (EP-14 HU-72, AC-1 y AC-3).
/// </summary>
public sealed class RecurringPaymentFiguresTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly October1 = new(2026, 10, 1);

    [Fact]
    public void Today_WeeklyPayment_CommitsEveryOccurrenceUntilMonthEnd()
    {
        // AC-1 de HU-72: 10.000 COP semanal desde el 6 de octubre, hoy 1 de octubre → 4 cobros = 40.000.
        var weekly = Make("Cleaning", 10000m, BillingCycle.Weekly, new DateOnly(2026, 10, 6));

        var cop = Assert.Single(DailyAllowanceCalculator.Calculate(
            October1, [new AccountBalance(AccountType.Bank, "COP", 1000000m, false)], [], [], [weekly]));

        Assert.Equal(43333.33m, weekly.GetMonthlyEquivalent());
        Assert.Equal(40000m, cop.CommittedUntilMonthEnd);
    }

    [Fact]
    public void Today_WeeklyPaymentOverdueForWeeks_CountsEachMissedOccurrenceOnce()
    {
        // Vencido desde el 15 de octubre, hoy 30: 15, 22 y 29 (vencidos) → 3 cobros.
        var weekly = Make("Cleaning", 10000m, BillingCycle.Weekly, new DateOnly(2026, 10, 15));

        var cop = Assert.Single(DailyAllowanceCalculator.Calculate(new DateOnly(2026, 10, 30), [], [], [], [weekly]));

        Assert.Equal(30000m, cop.CommittedUntilMonthEnd);
        Assert.Equal(
            [new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 29)],
            DailyAllowanceCalculator.ChargesUntil(weekly, new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Today_MonthlyAndYearly_StillCommitOneChargeInTheMonth()
    {
        var monthly = Make("Netflix", 38900m, BillingCycle.Monthly, new DateOnly(2026, 10, 28));
        var yearly = Make("Cloud", 120000m, BillingCycle.Yearly, new DateOnly(2026, 10, 10));

        var cop = Assert.Single(DailyAllowanceCalculator.Calculate(October1, [], [], [], [monthly, yearly]));

        Assert.Equal(158900m, cop.CommittedUntilMonthEnd);
    }

    [Fact]
    public void Analytics_WeeklyPayment_ExpectsFourOrFiveChargesPerMonth()
    {
        var weekly = Make("Cleaning", 10000m, BillingCycle.Weekly, new DateOnly(2026, 10, 6));

        var cop = Assert.Single(AnalyticsCalculator.Calculate([weekly], October1, 3));

        Assert.Equal([4, 4, 5], cop.ExpectedCharges.Select(c => c.Count));
        Assert.Equal([40000m, 40000m, 50000m], cop.ExpectedCharges.Select(c => c.Amount));
        Assert.Equal(43333.33m, cop.MonthlyTotal);
    }

    [Fact]
    public void Analytics_QuarterlyPayment_ExpectsAChargeEveryThreeMonths()
    {
        var quarterly = Make("Insurance", 300000m, BillingCycle.Quarterly, new DateOnly(2026, 11, 15));

        var cop = Assert.Single(AnalyticsCalculator.Calculate([quarterly], October1, 6));

        Assert.Equal([0, 1, 0, 0, 1, 0], cop.ExpectedCharges.Select(c => c.Count));
        Assert.Equal(100000m, cop.MonthlyTotal);
    }

    [Fact]
    public void Analytics_OverdueMonthlyAndYearly_KeepTheMvpExpectedCharges()
    {
        // AC-3 de HU-72: una mensual vencida se espera cada mes mostrado; una anual en su siguiente aniversario.
        var monthly = Make("Netflix", 38900m, BillingCycle.Monthly, new DateOnly(2026, 8, 31));
        var yearly = Make("Cloud", 120000m, BillingCycle.Yearly, new DateOnly(2026, 12, 15));

        var cop = Assert.Single(AnalyticsCalculator.Calculate([monthly, yearly], October1, 4));

        Assert.Equal([1, 1, 2, 1], cop.ExpectedCharges.Select(c => c.Count));
        Assert.Equal([38900m, 38900m, 158900m, 38900m], cop.ExpectedCharges.Select(c => c.Amount));
    }

    [Fact]
    public void SummaryAndSimulator_UseTheNewMonthlyEquivalents()
    {
        var weekly = Make("Cleaning", 10000m, BillingCycle.Weekly, new DateOnly(2026, 10, 6));
        var biweekly = Make("Babysitter", 10000m, BillingCycle.Biweekly, new DateOnly(2026, 10, 6));

        var summary = Assert.Single(SummaryCalculator.Calculate([weekly, biweekly]));
        var simulation = Assert.Single(SimulationCalculator.Calculate([weekly, biweekly], new HashSet<Guid> { weekly.Id }, [1]));

        Assert.Equal(65000.00m, summary.MonthlyEquivalent);
        Assert.Equal(43333.33m, simulation.SelectedMonthly);
    }

    private static Subscription Make(string name, decimal amount, BillingCycle cycle, DateOnly next) =>
        Subscription.Create(TestCurrentUser.DefaultSubject, name, null, amount, "COP", cycle, next, Clock);
}
