using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Dashboard;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Dashboard;

/// <summary>"Puedes gastar hoy" (HU-68, EP-12 §5): fórmula, monedas separadas y bordes del mes.</summary>
public sealed class DailyAllowanceCalculatorTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly October20 = new(2026, 10, 20);

    [Fact]
    public void Calculate_SpecExample_MatchesEveryInput()
    {
        // AC-1 de HU-68: efectivo 200.000 + banco 1.000.000, tarjeta debiendo 300.000, suscripción de 38.900 el 28, hoy 20.
        var result = DailyAllowanceCalculator.Calculate(
            October20,
            [Cop(AccountType.Cash, 200000m), Cop(AccountType.Bank, 1000000m), Cop(AccountType.CreditCard, -300000m)],
            [],
            [],
            [Monthly("Netflix", 38900m, new DateOnly(2026, 10, 28))]);

        var cop = Assert.Single(result);
        Assert.Equal(1200000m, cop.Available);
        Assert.Equal(38900m, cop.CommittedUntilMonthEnd);
        Assert.Equal(12, cop.DaysLeft);
        Assert.Equal(96758.33m, cop.SafeToSpendToday);
        Assert.Equal(0m, cop.Shortfall);
    }

    [Fact]
    public void Calculate_CommittedAboveAvailable_ReturnsZeroAndTheShortfall()
    {
        // AC-2 de HU-68.
        var result = DailyAllowanceCalculator.Calculate(
            October20, [Cop(AccountType.Bank, 30000m)], [], [], [Monthly("Gym", 80000m, new DateOnly(2026, 10, 25))]);

        var cop = Assert.Single(result);
        Assert.Equal(0m, cop.SafeToSpendToday);
        Assert.Equal(50000m, cop.Shortfall);
    }

    [Fact]
    public void Calculate_CurrencyWithAccountsButNoActivity_StillHasItsCardAndCurrenciesAreNeverSummed()
    {
        // AC-3 de HU-68.
        var result = DailyAllowanceCalculator.Calculate(
            October20,
            [Cop(AccountType.Bank, 1000m), new AccountBalance(AccountType.Savings, "USD", 50m, false)],
            [new PeriodTotals("COP", 500m, 200m)],
            [new PeriodTotals("COP", 0m, 50m)],
            []);

        Assert.Equal(["COP", "USD"], result.Select(r => r.Currency));
        Assert.Equal(new CurrencyAllowance("COP", 1000m, 500m, 200m, 300m, 50m, 0m, 12, 83.33m, 0m), result[0]);
        Assert.Equal(new CurrencyAllowance("USD", 50m, 0m, 0m, 0m, 0m, 0m, 12, 4.17m, 0m), result[1]);
    }

    [Fact]
    public void Calculate_ExcludesCardsArchivedAccountsInactiveAndLaterCharges_IncludesOverdue()
    {
        var inactive = Monthly("Old", 999m, new DateOnly(2026, 10, 22));
        inactive.Update("Old", null, 999m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 22), SubscriptionStatus.Inactive, Clock);

        var result = DailyAllowanceCalculator.Calculate(
            October20,
            [
                Cop(AccountType.Bank, 10000m),
                Cop(AccountType.CreditCard, 5000m),
                new AccountBalance(AccountType.Cash, "COP", 7000m, IsArchived: true),
            ],
            [],
            [],
            [
                Monthly("Overdue", 100m, new DateOnly(2026, 10, 5)),
                Monthly("Last day", 200m, new DateOnly(2026, 10, 31)),
                Monthly("Next month", 400m, new DateOnly(2026, 11, 1)),
                inactive,
            ]);

        var cop = Assert.Single(result);
        Assert.Equal(10000m, cop.Available);
        Assert.Equal(300m, cop.CommittedUntilMonthEnd);
    }

    [Fact]
    public void Calculate_ChargesInACurrencyWithoutAccounts_ShowAvailableZeroAndShortfall()
    {
        var result = DailyAllowanceCalculator.Calculate(
            October20, [Cop(AccountType.Bank, 100m)], [], [], [Monthly("Cloud", 9.99m, new DateOnly(2026, 10, 30), "USD")]);

        var usd = Assert.Single(result, r => r.Currency == "USD");
        Assert.Equal(0m, usd.Available);
        Assert.Equal(9.99m, usd.Shortfall);
        Assert.Equal(0m, usd.SafeToSpendToday);
    }

    [Theory]
    [InlineData(2026, 10, 31, 1)]
    [InlineData(2026, 10, 1, 31)]
    [InlineData(2028, 2, 1, 29)]
    [InlineData(2026, 2, 28, 1)]
    [InlineData(2026, 4, 15, 16)]
    public void DaysLeft_CountsTodayAndHandlesMonthLengths(int year, int month, int day, int expected)
    {
        Assert.Equal(expected, DailyAllowanceCalculator.DaysLeft(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Calculate_RoundsHalfAwayFromZeroToTwoDecimals()
    {
        // 0,25 / 2 días = 0,125 → 0,13 (AwayFromZero; el redondeo bancario daría 0,12).
        var result = DailyAllowanceCalculator.Calculate(
            new DateOnly(2026, 10, 30), [new AccountBalance(AccountType.Bank, "USD", 0.25m, false)], [], [], []);

        Assert.Equal(0.13m, Assert.Single(result).SafeToSpendToday);
    }

    private static AccountBalance Cop(AccountType type, decimal balance) => new(type, "COP", balance, false);

    private static Subscription Monthly(string name, decimal amount, DateOnly next, string currency = "COP") =>
        Subscription.Create("owner", name, null, amount, currency, BillingCycle.Monthly, next, Clock);
}
