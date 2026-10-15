using FlowTrim.Domain.Usage;

namespace FlowTrim.Application.Usage;

/// <summary>Petición de registro de uso. Campos anulables para validar presencia.</summary>
public sealed record RegisterUsageRequest(DateOnly? UsageDate, int? UsageCount, string? Notes);

/// <summary>Representación pública de un registro de uso.</summary>
public sealed record UsageRecordResponse(
    Guid Id,
    Guid SubscriptionId,
    DateOnly UsageDate,
    int UsageCount,
    string? Notes);

/// <summary>Mapeo explícito de la entidad de dominio al DTO de respuesta.</summary>
internal static class UsageRecordMapper
{
    public static UsageRecordResponse ToResponse(UsageRecord record) =>
        new(record.Id, record.SubscriptionId, record.UsageDate, record.UsageCount, record.Notes);
}
