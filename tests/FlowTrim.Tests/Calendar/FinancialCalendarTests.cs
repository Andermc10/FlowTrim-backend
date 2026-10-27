using FlowTrim.Domain.Calendar;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Calendar;

/// <summary>Calendario financiero (EP-14 HU-75): cobros esperados por día, lo registrado y totales por moneda.</summary>
public sealed class FinancialCalendarTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly October15 = new(2026, 10, 15);

    [Fact]
    public void Build_WeeklyAndMonthly_PlacesEachOccurrenceOnItsDayAndTotalsEqualTheDays()
    {
        // AC-1: semanal desde el 6 (6, 13, 20, 27) + mensual el 28 → 4 + 1 entradas; total = suma de los días.
        var weekly = Make("Cleaning", 10000m, BillingCycle.Weekly, new DateOnly(2026, 10, 6));
        var monthly = Make("Netflix", 38900m, BillingCycle.Monthly, new DateOnly(2026, 10, 28));

        var calendar = FinancialCalendar.Build(2026, 10, October15, [weekly, monthly], []);

        Assert.Equal(31, calendar.Days.Count);
        var withCharges = calendar.Days.Where(d => d.Expected.Count > 0).Select(d => d.Date.Day).ToList();
        Assert.Equal([6, 13, 20, 27, 28], withCharges);
        var cop = Assert.Single(calendar.Totals);
        Assert.Equal(78900m, cop.Expected);
        Assert.Equal(calendar.Days.SelectMany(d => d.Expected).Sum(c => c.Amount), cop.Expected);
        Assert.True(calendar.Days[5].Expected[0].IsOverdue);
        Assert.False(calendar.Days[19].Expected[0].IsOverdue);
    }

    [Fact]
    public void Build_PaymentAlreadyMarkedPaid_NeverShowsThatOccurrenceAgain()
    {
        // AC-2: Netflix vencía el 5; marcado como pagado su próxima fecha es el 5 de noviembre → octubre queda vacío.
        var netflix = Make("Netflix", 38900m, BillingCycle.Monthly, new DateOnly(2026, 10, 5));
        netflix.AdvanceBillingDate(Clock);

        var calendar = FinancialCalendar.Build(2026, 10, October15, [netflix], []);

        Assert.All(calendar.Days, day => Assert.Empty(day.Expected));
        Assert.Empty(calendar.Totals);
    }

    [Fact]
    public void Build_UnpaidOverdueCharge_StaysOnItsOriginalDate()
    {
        var gym = Make("Gym", 90000m, BillingCycle.Monthly, new DateOnly(2026, 10, 3));

        var calendar = FinancialCalendar.Build(2026, 10, October15, [gym], []);

        var charge = Assert.Single(calendar.Days[2].Expected);
        Assert.True(charge.IsOverdue);
    }

    [Fact]
    public void Build_RecordedTotals_StayPerDayAndCurrencyWithoutMixing()
    {
        var calendar = FinancialCalendar.Build(2026, 10, October15, [],
        [
            new DailyTotals(new DateOnly(2026, 10, 2), "COP", 0m, 25000m),
            new DailyTotals(new DateOnly(2026, 10, 2), "USD", 100m, 0m),
            new DailyTotals(new DateOnly(2026, 10, 9), "COP", 5000m, 10000m),
            new DailyTotals(new DateOnly(2026, 11, 1), "COP", 999m, 999m),
        ]);

        Assert.Equal(["COP", "USD"], calendar.Days[1].Recorded.Select(r => r.Currency));
        Assert.Equal(
            [new CalendarTotals("COP", 0m, 5000m, 35000m), new CalendarTotals("USD", 0m, 100m, 0m)],
            calendar.Totals);
    }

    [Fact]
    public void Build_InactivePaymentsAndCompletedPlans_ExpectNothing()
    {
        var inactive = Make("Old", 10m, BillingCycle.Monthly, new DateOnly(2026, 10, 10));
        inactive.Update(inactive.Name, null, 10m, "COP", BillingCycle.Monthly, inactive.NextBillingDate, SubscriptionStatus.Inactive, Clock);
        var plan = Subscription.Create("owner", "Laptop", null, 250000m, "COP", BillingCycle.Weekly, new DateOnly(2026, 10, 1), Clock,
            new RecurringPaymentDetails(SubscriptionKind.Instalment, null, null, null, 3, 1));

        var calendar = FinancialCalendar.Build(2026, 10, October15, [inactive, plan], []);

        Assert.Equal([1, 8], calendar.Days.Where(d => d.Expected.Count > 0).Select(d => d.Date.Day));
    }

    private static Subscription Make(string name, decimal amount, BillingCycle cycle, DateOnly next) =>
        Subscription.Create("owner", name, null, amount, "COP", cycle, next, Clock);
}
