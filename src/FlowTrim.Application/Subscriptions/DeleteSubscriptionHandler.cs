namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: eliminar una suscripción. Devuelve <c>false</c> si no existía.</summary>
public interface IDeleteSubscriptionHandler
{
    Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteSubscriptionHandler : IDeleteSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;

    public DeleteSubscriptionHandler(ISubscriptionRepository repository) => _repository = repository;

    public Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(id, cancellationToken);
}
