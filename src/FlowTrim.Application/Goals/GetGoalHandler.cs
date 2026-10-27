using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Caso de uso: una meta con sus últimos 50 aportes (EP-15 HU-77).</summary>
public interface IGetGoalHandler
{
    /// <summary>La meta, o <c>null</c> si no existe o es de otro propietario.</summary>
    Task<GoalDetailResponse?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GetGoalHandler : IGetGoalHandler
{
    public const int ContributionsLimit = 50;

    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetGoalHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<GoalDetailResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var goal = await _goals.GetByIdAsync(ownerId, id, cancellationToken);
        if (goal is null)
        {
            return null;
        }

        var today = ProductCalendar.Today(_timeProvider);
        var (from, to) = GoalCalculator.AveragePeriod(today);
        var totals = await _goals.GetTotalsAsync(ownerId, from, to, cancellationToken);
        var contributions = await _goals.ListContributionsAsync(ownerId, id, ContributionsLimit, cancellationToken);
        return new GoalDetailResponse(
            GoalMapper.ToResponse(goal, totals.GetValueOrDefault(goal.Id), today),
            contributions.Select(GoalMapper.ToResponse).ToList());
    }
}
