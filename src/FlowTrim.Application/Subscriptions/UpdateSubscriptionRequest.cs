namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Petición de actualización completa de una suscripción (reemplaza los campos editables).
/// Mismo contrato que la creación más <c>Status</c>. Campos anulables para validar presencia.
/// </summary>
public sealed record UpdateSubscriptionRequest(
    string? Name,
    string? Category,
    decimal? Amount,
    string? Currency,
    string? BillingCycle,
    DateOnly? NextBillingDate,
    string? Status);
