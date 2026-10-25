namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Proyección pura de los cobros esperados de un pago recurrente (EP-14 D4). "Hoy", la analítica y el calendario cuentan
/// los cobros con esta misma regla, para que un pago semanal (4 o 5 veces al mes) sume igual en todas las pantallas.
/// </summary>
public static class RecurrenceSchedule
{
    /// <summary>
    /// Fecha del cobro siguiente a <paramref name="date"/>: +7 días, +14 días, +1 mes, +3 meses o +1 año.
    /// <see cref="DateOnly.AddMonths"/> ajusta el fin de mes (31 ene → 28 feb) y los pasos siguientes parten de esa fecha,
    /// igual que marcar como pagado desde HU-57.
    /// </summary>
    public static DateOnly Next(DateOnly date, BillingCycle cycle) => cycle switch
    {
        BillingCycle.Weekly => date.AddDays(7),
        BillingCycle.Biweekly => date.AddDays(14),
        BillingCycle.Monthly => date.AddMonths(1),
        BillingCycle.Quarterly => date.AddMonths(3),
        BillingCycle.Yearly => date.AddYears(1),
        _ => throw new InvalidOperationException($"Unsupported billing cycle: {cycle}"),
    };

    /// <summary>
    /// Cobros esperados entre <paramref name="from"/> y <paramref name="to"/> (ambos incluidos), empezando en el próximo
    /// cobro y avanzando un ciclo cada vez. Los anteriores a <paramref name="from"/> se saltan, pero cuentan para el paso:
    /// para incluir los vencidos se pasa como <paramref name="from"/> la propia próxima fecha (o una anterior).
    /// </summary>
    public static IReadOnlyList<DateOnly> Occurrences(DateOnly nextDate, BillingCycle cycle, DateOnly from, DateOnly to)
    {
        var occurrences = new List<DateOnly>();
        for (var date = nextDate; date <= to; date = Next(date, cycle))
        {
            if (date >= from)
            {
                occurrences.Add(date);
            }
        }

        return occurrences;
    }

    /// <summary>
    /// Equivalente mensual de un importe según su ciclo (EP-14 §5): semanal × 52 / 12, quincenal × 26 / 12, mensual igual,
    /// trimestral / 3 y anual / 12, a 2 decimales con redondeo AwayFromZero. Es la estimación de un mes típico.
    /// </summary>
    public static decimal MonthlyEquivalent(decimal amount, BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => amount,
        BillingCycle.Weekly => Math.Round(amount * 52m / 12m, 2, MidpointRounding.AwayFromZero),
        BillingCycle.Biweekly => Math.Round(amount * 26m / 12m, 2, MidpointRounding.AwayFromZero),
        BillingCycle.Quarterly => Math.Round(amount / 3m, 2, MidpointRounding.AwayFromZero),
        BillingCycle.Yearly => Math.Round(amount / 12m, 2, MidpointRounding.AwayFromZero),
        _ => throw new InvalidOperationException($"Unsupported billing cycle: {cycle}"),
    };
}
