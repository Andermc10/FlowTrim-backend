namespace FlowTrim.Application.Usage;

/// <summary>Respuesta del costo declarado por uso de una suscripción.</summary>
public sealed record CostPerUseResponse(
    Guid SubscriptionId,
    string Currency,
    DateOnly? From,
    DateOnly? To,
    int MonthsInPeriod,
    decimal TotalPaid,
    long TotalUses,
    bool Calculable,
    decimal? CostPerUse,
    string? Reason);
