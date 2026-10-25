using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Acceso a persistencia para suscripciones. La implementación vive en Infrastructure.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Agrega una suscripción nueva y confirma los cambios.</summary>
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);

    /// <summary>Devuelve la suscripción del propietario por id, o <c>null</c> si no existe.</summary>
    Task<Subscription?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve una página del propietario ordenada por próxima fecha de cobro y nombre; solo del tipo
    /// <paramref name="kind"/> si se indica.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListAsync(
        string ownerId, SubscriptionKind? kind, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Cuenta las suscripciones del propietario (del tipo indicado, si lo hay) para la paginación.</summary>
    Task<int> CountAsync(string ownerId, SubscriptionKind? kind, CancellationToken cancellationToken);

    /// <summary>Devuelve las suscripciones activas del propietario para el resumen financiero.</summary>
    Task<IReadOnlyList<Subscription>> ListActiveAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Devuelve todas las suscripciones del propietario (activas e inactivas), ordenadas por nombre.</summary>
    Task<IReadOnlyList<Subscription>> ListAllAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Importación atómica (HU-45): en una sola transacción y en serie con otras importaciones del mismo propietario,
    /// lee sus suscripciones, deja que <paramref name="selectNew"/> decida cuáles crear y las inserta. Si algo falla no
    /// se crea ninguna.
    /// </summary>
    Task AddImportedAsync(
        string ownerId,
        Func<IReadOnlyList<Subscription>, IReadOnlyList<Subscription>> selectNew,
        CancellationToken cancellationToken);

    /// <summary>Persiste los cambios de una suscripción existente.</summary>
    Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken);

    /// <summary>Elimina la suscripción por id. Devuelve <c>true</c> si existía y se eliminó.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
