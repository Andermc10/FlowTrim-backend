namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Tipo de pago recurrente (EP-14 D2). Las suscripciones del MVP son <see cref="Subscription"/>; <see cref="Bill"/> cubre
/// arriendo, servicios, seguros o telefonía. Los planes de cuotas llegan con HU-74.
/// </summary>
public enum SubscriptionKind
{
    Subscription = 1,
    Bill = 2
}
