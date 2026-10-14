using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Acceso a persistencia para suscripciones. La implementación vive en Infrastructure.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Agrega una suscripción nueva y confirma los cambios.</summary>
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);

    /// <summary>Devuelve la suscripción por id, o <c>null</c> si no existe.</summary>
    Task<Subscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Devuelve una página de suscripciones ordenada por próxima fecha de cobro y nombre.</summary>
    Task<IReadOnlyList<Subscription>> ListAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>Cuenta el total de suscripciones (para la metadata de paginación).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Persiste los cambios de una suscripción existente.</summary>
    Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken);

    /// <summary>Elimina la suscripción por id. Devuelve <c>true</c> si existía y se eliminó.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
