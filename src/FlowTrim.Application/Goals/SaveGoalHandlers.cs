using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Caso de uso: crear una meta de ahorro (EP-15 HU-77).</summary>
public interface ICreateGoalHandler
{
    Task<HandlerResult<GoalResponse>> HandleAsync(GoalRequest request, CancellationToken cancellationToken);
}

/// <summary>Caso de uso: reemplazar los campos de una meta (EP-15 HU-77).</summary>
public interface IUpdateGoalHandler
{
    Task<HandlerResult<GoalResponse>> HandleAsync(Guid id, GoalRequest request, CancellationToken cancellationToken);
}

/// <summary>Validación de borde compartida por crear y editar: objetivo presente y fecha objetivo de hoy en adelante.</summary>
internal static class GoalRequestValidator
{
    public const string PastDateMessage = "The target date cannot be in the past.";

    /// <param name="keptDate">Fecha guardada: si no cambia se acepta aunque ya pasó (una meta vencida se puede editar).</param>
    public static Dictionary<string, string[]> Validate(GoalRequest request, DateOnly today, DateOnly? keptDate)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Target is null)
        {
            errors["target"] = ["The target amount is required."];
        }

        if (request.TargetDate is { } date && date < today && date != keptDate)
        {
            errors["targetDate"] = [PastDateMessage];
        }

        return errors;
    }
}

public sealed class CreateGoalHandler : ICreateGoalHandler
{
    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateGoalHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<GoalResponse>> HandleAsync(GoalRequest request, CancellationToken cancellationToken)
    {
        var today = ProductCalendar.Today(_timeProvider);
        var errors = GoalRequestValidator.Validate(request, today, null);
        if (errors.Count > 0)
        {
            return HandlerResult<GoalResponse>.Invalid(errors);
        }

        // Nombre, moneda y objetivo los valida el dominio (DomainValidationException → 400).
        var goal = SavingsGoal.Create(
            _currentUser.GetOwnerId(), request.Name, request.Currency, request.Target!.Value, request.TargetDate, _timeProvider);
        await _goals.AddAsync(goal, cancellationToken);
        return HandlerResult<GoalResponse>.Success(GoalMapper.ToResponse(goal, null, today));
    }
}

/// <summary>Editar una meta: la moneda queda fija cuando ya tiene aportes (409 <c>goal_currency_locked</c>).</summary>
public sealed class UpdateGoalHandler : IUpdateGoalHandler
{
    private readonly IGoalRepository _goals;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateGoalHandler(IGoalRepository goals, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _goals = goals;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<GoalResponse>> HandleAsync(Guid id, GoalRequest request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var goal = await _goals.GetByIdAsync(ownerId, id, cancellationToken);
        if (goal is null)
        {
            return HandlerResult<GoalResponse>.NotFound();
        }

        var today = ProductCalendar.Today(_timeProvider);
        var errors = GoalRequestValidator.Validate(request, today, goal.TargetDate);
        if (errors.Count > 0)
        {
            return HandlerResult<GoalResponse>.Invalid(errors);
        }

        var (from, to) = GoalCalculator.AveragePeriod(today);
        var totals = (await _goals.GetTotalsAsync(ownerId, from, to, cancellationToken)).GetValueOrDefault(goal.Id);
        if (totals is not null
            && !string.IsNullOrWhiteSpace(request.Currency)
            && !string.Equals(request.Currency.Trim(), goal.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return HandlerResult<GoalResponse>.Conflict(
                "goal_currency_locked", "The currency of a goal with contributions cannot change.");
        }

        goal.Update(request.Name, request.Currency, request.Target!.Value, request.TargetDate, _timeProvider);
        await _goals.UpdateAsync(goal, cancellationToken);
        return HandlerResult<GoalResponse>.Success(GoalMapper.ToResponse(goal, totals, today));
    }
}
