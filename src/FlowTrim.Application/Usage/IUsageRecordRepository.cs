using FlowTrim.Domain.Usage;

namespace FlowTrim.Application.Usage;

/// <summary>Acceso a persistencia para registros de uso. La implementación vive en Infrastructure.</summary>
public interface IUsageRecordRepository
{
    /// <summary>Agrega un registro de uso y confirma los cambios.</summary>
    Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken);

    /// <summary>
    /// Lista los usos de una suscripción, opcionalmente filtrados por rango de fechas (ambos inclusivos),
    /// ordenados por fecha descendente.
    /// </summary>
    Task<IReadOnlyList<UsageRecord>> ListBySubscriptionAsync(
        Guid subscriptionId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}
