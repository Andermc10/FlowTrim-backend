using FlowTrim.Application.Subscriptions;

namespace FlowTrim.Application.Usage;

/// <summary>Caso de uso: listar los usos de una suscripción por periodo. Devuelve null si no existe.</summary>
public interface IListUsageHandler
{
    Task<IReadOnlyList<UsageRecordResponse>?> HandleAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}

public sealed class ListUsageHandler : IListUsageHandler
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IUsageRecordRepository _usageRecordRepository;

    public ListUsageHandler(
        ISubscriptionRepository subscriptionRepository,
        IUsageRecordRepository usageRecordRepository)
    {
        _subscriptionRepository = subscriptionRepository;
        _usageRecordRepository = usageRecordRepository;
    }

    public async Task<IReadOnlyList<UsageRecordResponse>?> HandleAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var records = await _usageRecordRepository.ListBySubscriptionAsync(subscriptionId, from, to, cancellationToken);
        return records.Select(UsageRecordMapper.ToResponse).ToList();
    }
}
