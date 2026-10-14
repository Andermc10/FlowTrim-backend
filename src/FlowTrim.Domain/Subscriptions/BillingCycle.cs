namespace FlowTrim.Domain.Subscriptions;

/// <summary>Ciclo de cobro de una suscripción. El MVP solo admite mensual y anual.</summary>
public enum BillingCycle
{
    Monthly = 1,
    Yearly = 2
}
