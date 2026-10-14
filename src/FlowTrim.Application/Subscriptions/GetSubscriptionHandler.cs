namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: obtener una suscripción por id. Devuelve <c>null</c> si no existe.</summary>
public interface IGetSubscriptionHandler
{
    Task<SubscriptionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GetSubscriptionHandler : IGetSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;

    public GetSubscriptionHandler(ISubscriptionRepository repository) => _repository = repository;

    public async Task<SubscriptionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetByIdAsync(id, cancellationToken);
        return subscription is null ? null : SubscriptionMapper.ToResponse(subscription);
    }
}
