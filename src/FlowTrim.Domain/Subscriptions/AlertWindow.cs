namespace FlowTrim.Domain.Subscriptions;

/// <summary>Ventana de alerta de un cobro según los días que faltan respecto a la fecha de referencia.</summary>
public enum AlertWindow
{
    Overdue,
    Today,
    DueWithin3Days,
    DueWithin7Days,
    Later
}
