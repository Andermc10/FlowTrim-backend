using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Abstractions;
using FlowTrim.Domain.Usage;

namespace FlowTrim.Application.Usage;

public interface IGetCostPerUseHandler
{
    Task<CostPerUseResponse?> HandleAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}

/// <summary>Caso de uso para calcular el costo declarado por uso.</summary>
public sealed class GetCostPerUseHandler : IGetCostPerUseHandler
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IUsageRecordRepository _usageRecordRepository;
    private readonly ICurrentUser _currentUser;

    public GetCostPerUseHandler(
        ISubscriptionRepository subscriptionRepository,
        IUsageRecordRepository usageRecordRepository,
        ICurrentUser currentUser)
    {
        _subscriptionRepository = subscriptionRepository;
        _usageRecordRepository = usageRecordRepository;
        _currentUser = currentUser;
    }

    public async Task<CostPerUseResponse?> HandleAsync(
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

        var records = await _usageRecordRepository.ListBySubscriptionAsync(
            ownerId,
            subscriptionId,
            from,
            to,
            cancellationToken);
        var calculation = CostPerUseCalculator.Calculate(subscription, records, from, to);

        return new CostPerUseResponse(
            subscription.Id,
            subscription.Currency,
            calculation.From,
            calculation.To,
            calculation.MonthsInPeriod,
            calculation.TotalPaid,
            calculation.TotalUses,
            calculation.Calculable,
            calculation.CostPerUse,
            calculation.Reason);
    }
}
