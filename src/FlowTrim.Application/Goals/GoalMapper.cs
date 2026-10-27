using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Mapeo explícito de una meta y sus totales al DTO público, con las cifras de <see cref="GoalCalculator"/>.</summary>
internal static class GoalMapper
{
    public static GoalResponse ToResponse(SavingsGoal goal, GoalTotals? totals, DateOnly today)
    {
        var progress = GoalCalculator.Evaluate(goal.Target, goal.TargetDate, totals?.Saved ?? 0m, totals?.Recent ?? 0m, today);
        return new GoalResponse(
            goal.Id,
            goal.Name,
            goal.Currency,
            goal.Target,
            goal.TargetDate,
            progress.Saved,
            progress.Remaining,
            progress.Percent,
            progress.Completed,
            progress.IsOverdue,
            progress.MonthsLeft,
            progress.RequiredPerMonth,
            progress.AveragePerMonth,
            progress.EstimatedCompletion,
            totals?.Count ?? 0);
    }

    public static ContributionResponse ToResponse(GoalContribution contribution) =>
        new(contribution.Id, contribution.Amount, contribution.Date, contribution.Note, contribution.CreatedAt);
}
