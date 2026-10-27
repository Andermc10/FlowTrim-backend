using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Caso de uso: metas de ahorro con sus cifras (EP-15 HU-77). Activas primero por fecha objetivo; completadas al final.</summary>
public interface IListGoalsHandler
{
    Task<IReadOnlyList<GoalResponse>> HandleAsync(CancellationToken cancellationToken);
}

public sealed class ListGoalsHandler : IListGoalsHandler
{
    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public ListGoalsHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<GoalResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var today = ProductCalendar.Today(_timeProvider);
        var (from, to) = GoalCalculator.AveragePeriod(today);
        var goals = await _goals.ListAsync(ownerId, cancellationToken);
        var totals = await _goals.GetTotalsAsync(ownerId, from, to, cancellationToken);

        return goals
            .Select(goal => GoalMapper.ToResponse(goal, totals.GetValueOrDefault(goal.Id), today))
            .OrderBy(goal => goal.Completed)
            .ThenBy(goal => goal.TargetDate ?? DateOnly.MaxValue)
            .ThenBy(goal => goal.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
