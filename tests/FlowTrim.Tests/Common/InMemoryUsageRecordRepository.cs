using FlowTrim.Application.Usage;
using FlowTrim.Domain.Usage;

namespace FlowTrim.Tests.Common;

/// <summary>Repositorio de usos en memoria para pruebas.</summary>
public sealed class InMemoryUsageRecordRepository : IUsageRecordRepository
{
    public List<UsageRecord> Items { get; } = [];

    public Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken)
    {
        Items.Add(usageRecord);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageRecord>> ListBySubscriptionAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UsageRecord> records = Items
            .Where(u => u.SubscriptionId == subscriptionId)
            .Where(u => from is null || u.UsageDate >= from.Value)
            .Where(u => to is null || u.UsageDate <= to.Value)
            .OrderByDescending(u => u.UsageDate)
            .ToList();
        return Task.FromResult(records);
    }
}
