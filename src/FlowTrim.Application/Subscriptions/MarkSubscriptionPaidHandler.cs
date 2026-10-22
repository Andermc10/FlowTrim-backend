using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: marcar como pagado el cobro actual, que mueve la próxima fecha un ciclo (HU-57).</summary>
public interface IMarkSubscriptionPaidHandler
{
    Task<HandlerResult<SubscriptionResponse>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class MarkSubscriptionPaidHandler : IMarkSubscriptionPaidHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public MarkSubscriptionPaidHandler(ISubscriptionRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<SubscriptionResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetByIdAsync(_currentUser.GetOwnerId(), id, cancellationToken);
        if (subscription is null)
        {
            return HandlerResult<SubscriptionResponse>.NotFound();
        }

        // Una suscripción inactiva lanza DomainValidationException → 400 (manejador global).
        subscription.AdvanceBillingDate(_timeProvider);
        await _repository.UpdateAsync(subscription, cancellationToken);
        return HandlerResult<SubscriptionResponse>.Success(SubscriptionMapper.ToResponse(subscription));
    }
}
