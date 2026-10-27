using FlowTrim.Domain.Goals;

namespace FlowTrim.Application.Goals;

/// <summary>Petición de creación o reemplazo de una meta de ahorro (EP-15 HU-77).</summary>
public sealed record GoalRequest(string? Name, string? Currency, decimal? Target, DateOnly? TargetDate);

/// <summary>Aporte (positivo) o retiro (negativo) declarado.</summary>
public sealed record ContributionRequest(decimal? Amount, DateOnly? Date, string? Note);

public sealed record ContributionResponse(Guid Id, decimal Amount, DateOnly Date, string? Note, DateTimeOffset CreatedAt);

/// <summary>Meta con sus cifras calculadas (ninguna se guarda).</summary>
public sealed record GoalResponse(
    Guid Id,
    string Name,
    string Currency,
    decimal Target,
    DateOnly? TargetDate,
    decimal Saved,
    decimal Remaining,
    decimal Percent,
    bool Completed,
    bool IsOverdue,
    int? MonthsLeft,
    decimal? RequiredPerMonth,
    decimal AveragePerMonth,
    DateOnly? EstimatedCompletion,
    int ContributionsCount);

/// <summary>Meta con sus últimos aportes, más recientes primero.</summary>
public sealed record GoalDetailResponse(GoalResponse Goal, IReadOnlyList<ContributionResponse> Contributions);

/// <summary>Totales de los aportes de una meta, sumados en la base de datos.</summary>
public sealed record GoalTotals(Guid GoalId, decimal Saved, decimal Recent, int Count);

/// <summary>Acceso a persistencia de metas y aportes. La implementación vive en Infrastructure.</summary>
public interface IGoalRepository
{
    Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken);

    Task<SavingsGoal?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavingsGoal>> ListAsync(string ownerId, CancellationToken cancellationToken);

    Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken);

    /// <summary>Borra la meta y sus aportes. Devuelve <c>true</c> si existía.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Por meta: Σ de todos los aportes, Σ de los aportes entre <paramref name="recentFrom"/> y <paramref name="recentTo"/>
    /// (para el promedio) y cuántos hay. Las metas sin aportes no aparecen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, GoalTotals>> GetTotalsAsync(
        string ownerId, DateOnly recentFrom, DateOnly recentTo, CancellationToken cancellationToken);

    Task AddContributionAsync(GoalContribution contribution, CancellationToken cancellationToken);

    Task<IReadOnlyList<GoalContribution>> ListContributionsAsync(
        string ownerId, Guid goalId, int take, CancellationToken cancellationToken);

    Task<GoalContribution?> GetContributionAsync(string ownerId, Guid goalId, Guid id, CancellationToken cancellationToken);

    Task DeleteContributionAsync(GoalContribution contribution, CancellationToken cancellationToken);
}
