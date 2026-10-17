namespace FlowTrim.Application.Subscriptions;

using FlowTrim.Application.Abstractions;

/// <summary>Caso de uso: obtener una suscripción por id. Devuelve <c>null</c> si no existe.</summary>
public interface IGetSubscriptionHandler
{
    Task<SubscriptionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GetSubscriptionHandler : IGetSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetSubscriptionHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SubscriptionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetByIdAsync(_currentUser.GetOwnerId(), id, cancellationToken);
        return subscription is null ? null : SubscriptionMapper.ToResponse(subscription);
    }
}
