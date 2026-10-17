using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Abstractions;

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
    private readonly ICurrentUser _currentUser;

    public ListUsageHandler(
        ISubscriptionRepository subscriptionRepository,
        IUsageRecordRepository usageRecordRepository,
        ICurrentUser currentUser)
    {
        _subscriptionRepository = subscriptionRepository;
        _usageRecordRepository = usageRecordRepository;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UsageRecordResponse>?> HandleAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var subscription = await _subscriptionRepository.GetByIdAsync(ownerId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var records = await _usageRecordRepository.ListBySubscriptionAsync(ownerId, subscriptionId, from, to, cancellationToken);
        return records.Select(UsageRecordMapper.ToResponse).ToList();
    }
}
