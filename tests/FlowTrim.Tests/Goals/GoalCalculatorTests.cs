using FlowTrim.Domain.Common;
using FlowTrim.Domain.Goals;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Goals;

/// <summary>Cifras y proyección de una meta de ahorro (EP-15 HU-77, D7/D8). "Hoy" = 2026-10-20.</summary>
public sealed class GoalCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void Evaluate_TripWithTargetDate_RequiresAnEqualAmountEachMonth()
    {
        // AC-1: 3.000.000 para el 2027-03-31, ahorrado 600.000 → faltan 2.400.000 en 6 meses (oct–mar) → 400.000/mes.
        var progress = GoalCalculator.Evaluate(3000000m, new DateOnly(2027, 3, 31), 600000m, 0m, Today);

        Assert.Equal(2400000m, progress.Remaining);
        Assert.Equal(20.0m, progress.Percent);
        Assert.Equal(6, progress.MonthsLeft);
        Assert.Equal(400000m, progress.RequiredPerMonth);
        Assert.False(progress.IsOverdue);
        Assert.Null(progress.EstimatedCompletion);
    }

    [Fact]
    public void Evaluate_ThreeMonthsOfContributions_EstimatesTheCompletionMonth()
    {
        // AC-2: 300.000 en jul, ago y sep → promedio 300.000 → 2.400.000 / 300.000 = 8 meses después de octubre.
        var progress = GoalCalculator.Evaluate(3000000m, null, 600000m, 900000m, Today);

        Assert.Equal(300000m, progress.AveragePerMonth);
        Assert.Equal(new DateOnly(2027, 6, 1), progress.EstimatedCompletion);
        Assert.Null(progress.MonthsLeft);
        Assert.Null(progress.RequiredPerMonth);
    }

    [Fact]
    public void Evaluate_ReachedTarget_IsCompletedWithoutProjection()
    {
        // AC-3.
        var progress = GoalCalculator.Evaluate(1000m, new DateOnly(2027, 1, 31), 1200m, 900m, Today);

        Assert.True(progress.Completed);
        Assert.Equal(0m, progress.Remaining);
        Assert.Equal(120.0m, progress.Percent);
        Assert.Null(progress.RequiredPerMonth);
        Assert.Null(progress.EstimatedCompletion);
    }

    [Fact]
    public void Evaluate_PastTargetDate_IsOverdueWithOneMonthLeft()
    {
        var progress = GoalCalculator.Evaluate(1000m, new DateOnly(2026, 9, 30), 400m, 0m, Today);

        Assert.True(progress.IsOverdue);
        Assert.Equal(1, progress.MonthsLeft);
        Assert.Equal(600m, progress.RequiredPerMonth);
    }

    [Fact]
    public void Evaluate_NoRecentContributions_HasNoEstimate()
    {
        var progress = GoalCalculator.Evaluate(1000m, null, 100m, 0m, Today);

        Assert.Equal(0m, progress.AveragePerMonth);
        Assert.Null(progress.EstimatedCompletion);
    }

    [Fact]
    public void AveragePeriod_IsTheThreeFullMonthsBeforeTheCurrentOne()
    {
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)), GoalCalculator.AveragePeriod(Today));
        Assert.Equal((new DateOnly(2025, 10, 1), new DateOnly(2025, 12, 31)), GoalCalculator.AveragePeriod(new DateOnly(2026, 1, 5)));
    }

    [Theory]
    [InlineData("", "COP", 100, "name")]
    [InlineData("Trip", "pesos", 100, "currency")]
    [InlineData("Trip", "COP", 0, "target")]
    public void Create_InvalidGoal_FailsOnTheField(string name, string currency, double target, string field)
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            SavingsGoal.Create("owner", name, currency, (decimal)target, null, new FixedTimeProvider(DateTimeOffset.UnixEpoch)));

        Assert.Equal(field, exception.PropertyName);
    }

    [Fact]
    public void Contribution_ZeroAmount_IsRejected()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            GoalContribution.Create("owner", Guid.NewGuid(), 0m, Today, null, new FixedTimeProvider(DateTimeOffset.UnixEpoch)));

        Assert.Equal("amount", exception.PropertyName);
    }
}
