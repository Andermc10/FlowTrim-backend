using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Caso de uso: borrar una meta y sus aportes (EP-15 HU-77).</summary>
public interface IDeleteGoalHandler
{
    Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Caso de uso: declarar un aporte o un retiro (EP-15 HU-77).</summary>
public interface IAddContributionHandler
{
    Task<HandlerResult<ContributionResponse>> HandleAsync(Guid goalId, ContributionRequest request, CancellationToken cancellationToken);
}

/// <summary>Caso de uso: borrar un aporte (EP-15 HU-77).</summary>
public interface IDeleteContributionHandler
{
    Task<HandlerResult<Done>> HandleAsync(Guid goalId, Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteGoalHandler : IDeleteGoalHandler
{
    private readonly IGoalRepository _goals;
    private readonly ICurrentUser _currentUser;

    public DeleteGoalHandler(IGoalRepository goals, ICurrentUser currentUser)
    {
        _goals = goals;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await _goals.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken)
            ? HandlerResult<Done>.Success(Done.Value)
            : HandlerResult<Done>.NotFound();
}

/// <summary>
/// Un aporte tiene fecha de hoy o antes, y un retiro no puede dejar lo ahorrado por debajo de cero (EP-15 D7). El importe
/// y la nota los valida el dominio.
/// </summary>
public sealed class AddContributionHandler : IAddContributionHandler
{
    public const string FutureDateMessage = "The date cannot be in the future.";
    public const string NegativeSavedMessage = "A withdrawal cannot be larger than what is saved.";

    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public AddContributionHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<ContributionResponse>> HandleAsync(
        Guid goalId, ContributionRequest request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        if (await _goals.GetByIdAsync(ownerId, goalId, cancellationToken) is null)
        {
            return HandlerResult<ContributionResponse>.NotFound();
        }

        var today = ProductCalendar.Today(_timeProvider);
        var errors = new Dictionary<string, string[]>();
        if (request.Amount is null)
        {
            errors["amount"] = ["The amount is required."];
        }

        if (request.Date is null)
        {
            errors["date"] = ["The date is required."];
        }
        else if (request.Date > today)
        {
            errors["date"] = [FutureDateMessage];
        }

        if (errors.Count == 0 && request.Amount < 0m)
        {
            var (from, to) = GoalCalculator.AveragePeriod(today);
            var saved = (await _goals.GetTotalsAsync(ownerId, from, to, cancellationToken)).GetValueOrDefault(goalId)?.Saved ?? 0m;
            if (saved + request.Amount.Value < 0m)
            {
                errors["amount"] = [NegativeSavedMessage];
            }
        }

        if (errors.Count > 0)
        {
            return HandlerResult<ContributionResponse>.Invalid(errors);
        }

        var contribution = GoalContribution.Create(
            ownerId, goalId, request.Amount!.Value, request.Date!.Value, request.Note, _timeProvider);
        await _goals.AddContributionAsync(contribution, cancellationToken);
        return HandlerResult<ContributionResponse>.Success(GoalMapper.ToResponse(contribution));
    }
}

/// <summary>Borrar un aporte que deje lo ahorrado en negativo (un depósito ya retirado) responde 409 <c>goal_saved_negative</c>.</summary>
public sealed class DeleteContributionHandler : IDeleteContributionHandler
{
    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public DeleteContributionHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<Done>> HandleAsync(Guid goalId, Guid id, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var contribution = await _goals.GetContributionAsync(ownerId, goalId, id, cancellationToken);
        if (contribution is null)
        {
            return HandlerResult<Done>.NotFound();
        }

        var (from, to) = GoalCalculator.AveragePeriod(ProductCalendar.Today(_timeProvider));
        var saved = (await _goals.GetTotalsAsync(ownerId, from, to, cancellationToken)).GetValueOrDefault(goalId)?.Saved ?? 0m;
        if (saved - contribution.Amount < 0m)
        {
            return HandlerResult<Done>.Conflict(
                "goal_saved_negative", "Deleting this contribution would leave the goal below zero. Delete the withdrawal first.");
        }

        await _goals.DeleteContributionAsync(contribution, cancellationToken);
        return HandlerResult<Done>.Success(Done.Value);
    }
}
