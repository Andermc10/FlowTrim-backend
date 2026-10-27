using FlowTrim.Application.Budgets;
using FlowTrim.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="IBudgetRepository"/> (EP-15 HU-76).</summary>
internal sealed class BudgetRepository : IBudgetRepository
{
    private readonly FlowTrimDbContext _dbContext;

    public BudgetRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Budget budget, CancellationToken cancellationToken)
    {
        _dbContext.Budgets.Add(budget);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Budget?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Budgets.FirstOrDefaultAsync(b => b.OwnerId == ownerId && b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Budgets.Where(b => b.OwnerId == ownerId).ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(string ownerId, Guid categoryId, string currency, CancellationToken cancellationToken) =>
        await _dbContext.Budgets.AnyAsync(
            b => b.OwnerId == ownerId && b.CategoryId == categoryId && b.Currency == currency, cancellationToken);

    public async Task UpdateAsync(Budget budget, CancellationToken cancellationToken)
    {
        _dbContext.Budgets.Update(budget);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Budgets.Where(b => b.OwnerId == ownerId && b.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
}
