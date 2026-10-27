using System.Globalization;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Calendar;

namespace FlowTrim.Application.Calendar;

/// <summary>Cobro esperado de un día (no un pago). <c>Kind</c> como texto, igual que en el resto de la API.</summary>
public sealed record CalendarExpectedResponse(
    Guid SubscriptionId, string Name, string Kind, decimal Amount, string Currency, bool IsOverdue, string? PaymentUrl);

/// <summary>Un día del mes: cobros esperados y lo registrado por moneda.</summary>
public sealed record CalendarDayResponse(
    DateOnly Date, IReadOnlyList<CalendarExpectedResponse> Expected, IReadOnlyList<RecordedTotals> Recorded);

/// <summary>Calendario financiero de un mes (EP-14 HU-75): días, totales por moneda y el "hoy" del producto.</summary>
public sealed record CalendarResponse(
    string Month, DateOnly Today, IReadOnlyList<CalendarDayResponse> Days, IReadOnlyList<CalendarTotals> Totals);

/// <summary>Caso de uso: calendario financiero del mes pedido (por defecto, el mes de hoy).</summary>
public interface IGetCalendarHandler
{
    Task<HandlerResult<CalendarResponse>> HandleAsync(string? month, CancellationToken cancellationToken);
}

/// <summary>
/// Reúne los pagos activos y los totales diarios registrados (sumados en SQL) y delega en
/// <see cref="FinancialCalendar"/>, que es puro. "Hoy" sale del calendario del producto.
/// </summary>
public sealed class GetCalendarHandler : IGetCalendarHandler
{
    public const string MonthFormatMessage = "Month must use the yyyy-MM format.";

    private readonly ISubscriptionRepository _subscriptions;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetCalendarHandler(
        ISubscriptionRepository subscriptions,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _subscriptions = subscriptions;
        _transactions = transactions;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<CalendarResponse>> HandleAsync(string? month, CancellationToken cancellationToken)
    {
        var today = ProductCalendar.Today(_timeProvider);
        var first = new DateOnly(today.Year, today.Month, 1);
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
            {
                return HandlerResult<CalendarResponse>.Invalid(new Dictionary<string, string[]> { ["month"] = [MonthFormatMessage] });
            }
        }

        var ownerId = _currentUser.GetOwnerId();
        var subscriptions = await _subscriptions.ListActiveAsync(ownerId, cancellationToken);
        var recorded = await _transactions.GetDailyTotalsAsync(ownerId, first, first.AddMonths(1).AddDays(-1), cancellationToken);
        var calendar = FinancialCalendar.Build(first.Year, first.Month, today, subscriptions, recorded);

        return HandlerResult<CalendarResponse>.Success(new CalendarResponse(
            first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            today,
            calendar.Days.Select(day => new CalendarDayResponse(
                day.Date,
                day.Expected.Select(c => new CalendarExpectedResponse(
                    c.SubscriptionId, c.Name, c.Kind.ToString(), c.Amount, c.Currency, c.IsOverdue, c.PaymentUrl)).ToList(),
                day.Recorded)).ToList(),
            calendar.Totals));
    }
}
