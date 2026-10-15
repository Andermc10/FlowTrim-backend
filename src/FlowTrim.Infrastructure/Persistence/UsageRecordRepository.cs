using FlowTrim.Application.Usage;
using FlowTrim.Domain.Usage;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="IUsageRecordRepository"/>.</summary>
internal sealed class UsageRecordRepository : IUsageRecordRepository
{
    private readonly FlowTrimDbContext _dbContext;

    public UsageRecordRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken)
    {
        await _dbContext.UsageRecords.AddAsync(usageRecord, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageRecord>> ListBySubscriptionAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.UsageRecords.Where(u => u.SubscriptionId == subscriptionId);

        if (from is not null)
        {
            query = query.Where(u => u.UsageDate >= from.Value);
        }

        if (to is not null)
        {
            query = query.Where(u => u.UsageDate <= to.Value);
        }

        return await query.OrderByDescending(u => u.UsageDate).ToListAsync(cancellationToken);
    }
}
