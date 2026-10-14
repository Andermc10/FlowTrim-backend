using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Acceso a persistencia para suscripciones. La implementación vive en Infrastructure.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Agrega una suscripción nueva y confirma los cambios.</summary>
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);
}
