using FlowTrim.Application.Simulations;
using FlowTrim.Domain.Simulations;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Simulations;

public sealed class SimulationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly SampleDate = new(2026, 11, 1);
    private static readonly int[] Horizons = [1, 12, 24];

    private static Subscription Make(decimal amount, string currency = "COP", BillingCycle cycle = BillingCycle.Monthly, bool active = true)
    {
        var subscription = Subscription.Create("Service", null, amount, currency, cycle, SampleDate, new FixedTimeProvider(FixedNow));
        if (!active)
        {
            subscription.Update("Service", null, amount, currency, cycle, SampleDate, SubscriptionStatus.Inactive, new FixedTimeProvider(FixedNow));
        }

        return subscription;
    }

    // ----- SimulationCalculator -----

    [Fact]
    public void Calculate_SavingsIsSelectedMonthlyTimesHorizon()
    {
        var selected = Make(38900m);
        var kept = Make(10000m);
        var active = new[] { selected, kept };

        var result = SimulationCalculator.Calculate(active, new HashSet<Guid> { selected.Id }, Horizons);

        var cop = Assert.Single(result);
        Assert.Equal(48900m, cop.CurrentMonthly);
        Assert.Equal(38900m, cop.SelectedMonthly);
        Assert.Equal(10000m, cop.ProjectedMonthly);
        Assert.Equal(38900m, cop.Savings.Single(s => s.Months == 1).Amount);
        Assert.Equal(466800m, cop.Savings.Single(s => s.Months == 12).Amount);
        Assert.Equal(933600m, cop.Savings.Single(s => s.Months == 24).Amount);
    }

    [Fact]
    public void Calculate_EmptySelection_ReturnsZeroSavings()
    {
        var active = new[] { Make(38900m) };

        var result = SimulationCalculator.Calculate(active, new HashSet<Guid>(), Horizons);

        var cop = Assert.Single(result);
        Assert.Equal(0m, cop.SelectedMonthly);
        Assert.Equal(cop.CurrentMonthly, cop.ProjectedMonthly);
        Assert.All(cop.Savings, s => Assert.Equal(0m, s.Amount));
    }

    [Fact]
    public void Calculate_GroupsByCurrency_WithoutSummingAcross()
    {
        var cop = Make(38900m, "COP");
        var usd = Make(10m, "USD");

        var result = SimulationCalculator.Calculate(new[] { cop, usd }, new HashSet<Guid> { cop.Id, usd.Id }, Horizons);

        Assert.Equal(2, result.Count);
        Assert.Equal("COP", result[0].Currency);
        Assert.Equal("USD", result[1].Currency);
        Assert.Equal(38900m, result[0].SelectedMonthly);
        Assert.Equal(10m, result[1].SelectedMonthly);
    }

    [Fact]
    public void Calculate_IgnoresInactiveSelection()
    {
        var inactive = Make(50000m, active: false);
        var activeSub = Make(10000m);

        // El inactivo no está en el conjunto activo que recibe el calculador.
        var result = SimulationCalculator.Calculate(new[] { activeSub }, new HashSet<Guid> { inactive.Id, activeSub.Id }, Horizons);

        var cop = Assert.Single(result);
        Assert.Equal(10000m, cop.SelectedMonthly);
    }

    // ----- SimulateHandler -----

    [Fact]
    public async Task Simulate_DefaultsHorizonsWhenOmitted()
    {
        var repo = new InMemorySubscriptionRepository();
        repo.Items.Add(Make(38900m));
        var handler = new SimulateHandler(repo);

        var result = await handler.HandleAsync(new SimulationRequest(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 12, 24], result.Value!.Horizons);
        Assert.NotEmpty(result.Value.Assumptions);
    }

    [Fact]
    public async Task Simulate_InvalidHorizon_ReturnsInvalid()
    {
        var handler = new SimulateHandler(new InMemorySubscriptionRepository());

        var result = await handler.HandleAsync(new SimulationRequest(null, [0]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.ContainsKey("horizons"));
    }

    [Fact]
    public async Task Simulate_BuildsPerCurrencyResult()
    {
        var repo = new InMemorySubscriptionRepository();
        var netflix = Make(38900m, "COP");
        repo.Items.Add(netflix);
        repo.Items.Add(Make(10000m, "COP"));
        var handler = new SimulateHandler(repo);

        var result = await handler.HandleAsync(new SimulationRequest([netflix.Id], [12]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var cop = Assert.Single(result.Value!.Currencies);
        Assert.Equal(48900m, cop.CurrentMonthly);
        Assert.Equal(38900m, cop.SelectedMonthly);
        Assert.Equal(466800m, cop.Savings.Single(s => s.Months == 12).Amount);
    }
}
