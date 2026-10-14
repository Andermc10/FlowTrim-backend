using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Mapeo explícito de la entidad de dominio al DTO público de respuesta.</summary>
internal static class SubscriptionMapper
{
    public static SubscriptionResponse ToResponse(Subscription subscription) => new(
        subscription.Id,
        subscription.Name,
        subscription.Category,
        subscription.Amount,
        subscription.Currency,
        subscription.BillingCycle.ToString(),
        subscription.NextBillingDate,
        subscription.Status.ToString(),
        subscription.CreatedAt,
        subscription.UpdatedAt);
}
