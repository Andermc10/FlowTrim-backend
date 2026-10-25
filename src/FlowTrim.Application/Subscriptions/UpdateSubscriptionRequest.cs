namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Petición de actualización completa de una suscripción (reemplaza los campos editables).
/// Mismo contrato que la creación más <c>Status</c>. Campos anulables para validar presencia. <c>Kind</c> ausente
/// conserva el tipo actual; enlace, cuenta y categoría por defecto ausentes se borran (reemplazo completo).
/// </summary>
public sealed record UpdateSubscriptionRequest(
    string? Name,
    string? Category,
    decimal? Amount,
    string? Currency,
    string? BillingCycle,
    DateOnly? NextBillingDate,
    string? Status,
    string? Kind = null,
    string? PaymentUrl = null,
    Guid? DefaultAccountId = null,
    Guid? CategoryId = null);
