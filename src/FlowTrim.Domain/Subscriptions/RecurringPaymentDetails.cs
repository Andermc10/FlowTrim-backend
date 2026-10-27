namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Datos de pago recurrente añadidos con EP-14 (HU-72): tipo, enlace de pago opcional y cuenta y categoría por defecto
/// para registrar el pago. Que la cuenta y la categoría pertenezcan al propietario lo valida la capa de aplicación.
/// En un plan de cuotas (HU-74) <c>TotalInstalments</c> es obligatorio y <c>PaidInstalments</c> solo cuenta al crear
/// (planes ya empezados); después lo lleva "marcar como pagado".
/// </summary>
public sealed record RecurringPaymentDetails(
    SubscriptionKind Kind,
    string? PaymentUrl,
    Guid? DefaultAccountId,
    Guid? CategoryId,
    int? TotalInstalments = null,
    int? PaidInstalments = null)
{
    /// <summary>Valores de una suscripción del MVP: sin enlace ni valores por defecto.</summary>
    public static RecurringPaymentDetails Default { get; } = new(SubscriptionKind.Subscription, null, null, null);
}
