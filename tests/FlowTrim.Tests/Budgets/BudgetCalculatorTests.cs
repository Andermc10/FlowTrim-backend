using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Common;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Budgets;

/// <summary>Avance y estados de un presupuesto (EP-15 HU-76, D3) y sus invariantes.</summary>
public sealed class BudgetCalculatorTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(250000, 62.5, BudgetState.OnTrack, 150000, 0)]
    [InlineData(319999, 80.0, BudgetState.Warning, 80001, 0)]
    [InlineData(330000, 82.5, BudgetState.Warning, 70000, 0)]
    [InlineData(399999, 100.0, BudgetState.Warning, 1, 0)]
    [InlineData(400000, 100.0, BudgetState.Exceeded, 0, 0)]
    [InlineData(430000, 107.5, BudgetState.Exceeded, 0, 30000)]
    [InlineData(0, 0.0, BudgetState.OnTrack, 400000, 0)]
    public void Evaluate_FoodBudget_MatchesTheSpecThresholds(
        double spent, double percent, BudgetState state, double remaining, double over)
    {
        // AC-1: Comida 400.000 COP. Se excede solo cuando lo gastado alcanza el límite, aunque 99,99 % redondee a 100,0.
        var progress = BudgetCalculator.Evaluate(400000m, (decimal)spent);

        Assert.Equal((decimal)percent, progress.Percent);
        Assert.Equal(state, progress.State);
        Assert.Equal((decimal)remaining, progress.Remaining);
        Assert.Equal((decimal)over, progress.Over);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.123)]
    public void Create_InvalidLimit_FailsOnLimit(double limit)
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => Budget.Create("owner", Guid.NewGuid(), "COP", (decimal)limit, Clock));

        Assert.Equal("limit", exception.PropertyName);
    }

    [Fact]
    public void Create_NormalizesTheCurrencyAndRejectsBadOnes()
    {
        Assert.Equal("USD", Budget.Create("owner", Guid.NewGuid(), " usd ", 10m, Clock).Currency);
        Assert.Equal("currency", Assert.Throws<DomainValidationException>(
            () => Budget.Create("owner", Guid.NewGuid(), "dollars", 10m, Clock)).PropertyName);
    }
}
