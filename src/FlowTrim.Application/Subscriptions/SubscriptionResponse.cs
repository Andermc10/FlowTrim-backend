namespace FlowTrim.Application.Subscriptions;

/// <summary>Representación pública de una suscripción. Los enums se exponen como su nombre (texto).</summary>
public sealed record SubscriptionResponse(
    Guid Id,
    string Name,
    string? Category,
    decimal Amount,
    string Currency,
    string BillingCycle,
    DateOnly NextBillingDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
