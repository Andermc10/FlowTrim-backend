using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;

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
}
