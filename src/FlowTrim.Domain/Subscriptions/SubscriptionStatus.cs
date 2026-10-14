namespace FlowTrim.Domain.Subscriptions;

/// <summary>Estado de una suscripción. Las inactivas se excluyen del gasto activo.</summary>
public enum SubscriptionStatus
{
    Active = 1,
    Inactive = 2
}
