using FlowTrim.Application.Goals;
using FlowTrim.Domain.Goals;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="IGoalRepository"/> (EP-15 HU-77).</summary>
internal sealed class GoalRepository : IGoalRepository
{
    private readonly FlowTrimDbContext _dbContext;

    public GoalRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken)
    {
        _dbContext.SavingsGoals.Add(goal);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SavingsGoal?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.SavingsGoals.FirstOrDefaultAsync(g => g.OwnerId == ownerId && g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SavingsGoal>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        await _dbContext.SavingsGoals.Where(g => g.OwnerId == ownerId).ToListAsync(cancellationToken);

    public async Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken)
    {
        _dbContext.SavingsGoals.Update(goal);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.SavingsGoals.Where(g => g.OwnerId == ownerId && g.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyDictionary<Guid, GoalTotals>> GetTotalsAsync(
        string ownerId, DateOnly recentFrom, DateOnly recentTo, CancellationToken cancellationToken)
    {
        // Sumado en SQL por meta: todo lo aportado y lo aportado en el periodo del promedio.
        var rows = await _dbContext.GoalContributions
            .Where(c => c.OwnerId == ownerId)
            .GroupBy(c => c.GoalId)
            .Select(goal => new
            {
                GoalId = goal.Key,
                Saved = goal.Sum(c => c.Amount),
                Recent = goal.Sum(c => c.Date >= recentFrom && c.Date <= recentTo ? c.Amount : 0m),
                Count = goal.Count(),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.GoalId, row => new GoalTotals(row.GoalId, row.Saved, row.Recent, row.Count));
    }

    public async Task AddContributionAsync(GoalContribution contribution, CancellationToken cancellationToken)
    {
        _dbContext.GoalContributions.Add(contribution);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GoalContribution>> ListContributionsAsync(
        string ownerId, Guid goalId, int take, CancellationToken cancellationToken) =>
        await _dbContext.GoalContributions
            .Where(c => c.OwnerId == ownerId && c.GoalId == goalId)
            .OrderByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<GoalContribution?> GetContributionAsync(
        string ownerId, Guid goalId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.GoalContributions.FirstOrDefaultAsync(
            c => c.OwnerId == ownerId && c.GoalId == goalId && c.Id == id, cancellationToken);

    public async Task DeleteContributionAsync(GoalContribution contribution, CancellationToken cancellationToken)
    {
        _dbContext.GoalContributions.Remove(contribution);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
