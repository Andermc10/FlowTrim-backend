using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="ISubscriptionRepository"/>.</summary>
internal sealed class SubscriptionRepository : ISubscriptionRepository
{
    private readonly FlowTrimDbContext _dbContext;

    public SubscriptionRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        await _dbContext.Subscriptions.AddAsync(subscription, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Subscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Subscription>> ListAsync(int skip, int take, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions
            .OrderBy(s => s.NextBillingDate)
            .ThenBy(s => s.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Subscription>> ListActiveAsync(CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        _dbContext.Subscriptions.Update(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        _dbContext.Subscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
