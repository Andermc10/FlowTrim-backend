using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Calendar;

/// <summary>Ingresos y gastos registrados un día en una moneda (las transferencias ya están excluidas).</summary>
public sealed record DailyTotals(DateOnly Date, string Currency, decimal Income, decimal Expense);

/// <summary>Cobro esperado (no un pago) de un pago recurrente en un día; vencido si el día ya pasó.</summary>
public sealed record ExpectedCharge(
    Guid SubscriptionId, string Name, SubscriptionKind Kind, decimal Amount, string Currency, bool IsOverdue, string? PaymentUrl);

/// <summary>Lo registrado un día en una moneda.</summary>
public sealed record RecordedTotals(string Currency, decimal Income, decimal Expense);

/// <summary>Un día del mes con lo esperado y lo registrado.</summary>
public sealed record CalendarDay(DateOnly Date, IReadOnlyList<ExpectedCharge> Expected, IReadOnlyList<RecordedTotals> Recorded);

/// <summary>Totales del mes por moneda; nunca se suman monedas distintas.</summary>
public sealed record CalendarTotals(string Currency, decimal Expected, decimal RecordedIncome, decimal RecordedExpense);

/// <summary>Mes del calendario: todos sus días y los totales por moneda.</summary>
public sealed record CalendarMonth(IReadOnlyList<CalendarDay> Days, IReadOnlyList<CalendarTotals> Totals);

/// <summary>
/// Calendario financiero del mes (EP-14 HU-75, D10). Función pura: los cobros esperados salen de
/// <see cref="Subscription.GetOccurrences"/> (la misma proyección de "Hoy" y la analítica) desde la próxima fecha de cada
/// pago activo, así que un cobro ya marcado como pagado nunca reaparece y uno sin pagar sigue en su fecha original,
/// marcado como vencido. Lo registrado son los movimientos que el usuario confirmó; FlowTrim no inventa pagos.
/// </summary>
public static class FinancialCalendar
{
    public static CalendarMonth Build(
        int year, int month, DateOnly today, IEnumerable<Subscription> subscriptions, IEnumerable<DailyTotals> recorded)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var expectedByDay = subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .SelectMany(s => s.GetOccurrences(first, last).Select(date => (Date: date, Charge: new ExpectedCharge(
                s.Id, s.Name, s.Kind, s.Amount, s.Currency, date < today, s.PaymentUrl))))
            .ToLookup(item => item.Date, item => item.Charge);
        var recordedRows = recorded.Where(row => row.Date >= first && row.Date <= last).ToList();
        var recordedByDay = recordedRows.ToLookup(row => row.Date);

        var days = Enumerable.Range(0, last.Day)
            .Select(offset => first.AddDays(offset))
            .Select(date => new CalendarDay(
                date,
                expectedByDay[date].OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
                recordedByDay[date]
                    .OrderBy(r => r.Currency, StringComparer.Ordinal)
                    .Select(r => new RecordedTotals(r.Currency, r.Income, r.Expense))
                    .ToList()))
            .ToList();

        // Los totales son exactamente la suma de las entradas de los días (AC-1).
        var expected = days.SelectMany(d => d.Expected).GroupBy(c => c.Currency).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        var income = recordedRows.GroupBy(r => r.Currency).ToDictionary(g => g.Key, g => g.Sum(r => r.Income));
        var expense = recordedRows.GroupBy(r => r.Currency).ToDictionary(g => g.Key, g => g.Sum(r => r.Expense));
        var totals = expected.Keys.Concat(income.Keys)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(currency => new CalendarTotals(
                currency,
                expected.GetValueOrDefault(currency),
                income.GetValueOrDefault(currency),
                expense.GetValueOrDefault(currency)))
            .ToList();

        return new CalendarMonth(days, totals);
    }
}
