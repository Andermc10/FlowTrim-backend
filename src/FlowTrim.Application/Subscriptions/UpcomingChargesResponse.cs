namespace FlowTrim.Application.Subscriptions;

/// <summary>Próximos cobros ordenados por fecha, con la fecha de referencia usada por el servidor.</summary>
public sealed record UpcomingChargesResponse(DateOnly ReferenceDate, IReadOnlyList<UpcomingChargeItem> Items);

/// <summary>Un cobro próximo con su cuenta regresiva y ventana de alerta (calculadas en el servidor).</summary>
public sealed record UpcomingChargeItem(
    Guid Id,
    string Name,
    decimal Amount,
    string Currency,
    string BillingCycle,
    DateOnly NextBillingDate,
    int DaysUntilCharge,
    string AlertWindow);
