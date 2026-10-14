namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Petición de creación de una suscripción. Los campos son anulables para poder distinguir
/// "ausente" de un valor inválido y devolver un 400 por campo. El enum llega como texto
/// (p. ej. "Monthly") para validar y reportar valores no admitidos de forma limpia.
/// </summary>
public sealed record CreateSubscriptionRequest(
    string? Name,
    string? Category,
    decimal? Amount,
    string? Currency,
    string? BillingCycle,
    DateOnly? NextBillingDate);
