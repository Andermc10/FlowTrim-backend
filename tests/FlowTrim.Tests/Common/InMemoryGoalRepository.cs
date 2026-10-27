using FlowTrim.Application.Goals;
using FlowTrim.Domain.Goals;

namespace FlowTrim.Tests.Common;

/// <summary>Repositorio de metas en memoria para las pruebas de API (EP-15 HU-77).</summary>
public sealed class InMemoryGoalRepository : IGoalRepository
{
    public List<SavingsGoal> Goals { get; } = [];
    public List<GoalContribution> Contributions { get; } = [];

    public Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken)
    {
        Goals.Add(goal);
        return Task.CompletedTask;
    }

    public Task<SavingsGoal?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Goals.FirstOrDefault(g => g.OwnerId == ownerId && g.Id == id));

    public Task<IReadOnlyList<SavingsGoal>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavingsGoal>>(Goals.Where(g => g.OwnerId == ownerId).ToList());

    public Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        Contributions.RemoveAll(c => c.OwnerId == ownerId && c.GoalId == id);
        return Task.FromResult(Goals.RemoveAll(g => g.OwnerId == ownerId && g.Id == id) > 0);
    }

    public Task<IReadOnlyDictionary<Guid, GoalTotals>> GetTotalsAsync(
        string ownerId, DateOnly recentFrom, DateOnly recentTo, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, GoalTotals> totals = Contributions
            .Where(c => c.OwnerId == ownerId)
            .GroupBy(c => c.GoalId)
            .ToDictionary(
                group => group.Key,
                group => new GoalTotals(
                    group.Key,
                    group.Sum(c => c.Amount),
                    group.Where(c => c.Date >= recentFrom && c.Date <= recentTo).Sum(c => c.Amount),
                    group.Count()));
        return Task.FromResult(totals);
    }

    public Task AddContributionAsync(GoalContribution contribution, CancellationToken cancellationToken)
    {
        Contributions.Add(contribution);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GoalContribution>> ListContributionsAsync(
        string ownerId, Guid goalId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GoalContribution>>(Contributions
            .Where(c => c.OwnerId == ownerId && c.GoalId == goalId)
            .OrderByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .Take(take)
            .ToList());

    public Task<GoalContribution?> GetContributionAsync(string ownerId, Guid goalId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Contributions.FirstOrDefault(c => c.OwnerId == ownerId && c.GoalId == goalId && c.Id == id));

    public Task DeleteContributionAsync(GoalContribution contribution, CancellationToken cancellationToken)
    {
        Contributions.Remove(contribution);
        return Task.CompletedTask;
    }
}
