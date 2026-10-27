namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Tipo de pago recurrente (EP-14 D2). Las suscripciones del MVP son <see cref="Subscription"/>; <see cref="Bill"/> cubre
/// arriendo, servicios, seguros o telefonía; <see cref="Instalment"/> es un plan con un número fijo de cuotas (HU-74).
/// </summary>
public enum SubscriptionKind
{
    Subscription = 1,
    Bill = 2,
    Instalment = 3
}
