namespace FlowTrim.Application.Subscriptions;

using FlowTrim.Application.Abstractions;

/// <summary>Caso de uso: eliminar una suscripción. Devuelve <c>false</c> si no existía.</summary>
public interface IDeleteSubscriptionHandler
{
    Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteSubscriptionHandler : IDeleteSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public DeleteSubscriptionHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public Task<bool> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken);
}
