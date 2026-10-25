namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Ciclo de cobro de un pago recurrente. Mensual y anual vienen del MVP; semanal, quincenal (cada 14 días) y trimestral
/// se añadieron con EP-14 (D3). Los valores numéricos no se reutilizan: en la base de datos se guarda el nombre.
/// </summary>
public enum BillingCycle
{
    Monthly = 1,
    Yearly = 2,
    Weekly = 3,
    Biweekly = 4,
    Quarterly = 5
}
