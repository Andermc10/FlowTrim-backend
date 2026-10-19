namespace FlowTrim.Application.Subscriptions;

using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;

/// <summary>Caso de uso: listar los próximos cobros de las suscripciones activas con su cuenta regresiva.</summary>
public interface IGetUpcomingChargesHandler
{
    Task<UpcomingChargesResponse> HandleAsync(CancellationToken cancellationToken);
}

public sealed class GetUpcomingChargesHandler : IGetUpcomingChargesHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetUpcomingChargesHandler(ISubscriptionRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<UpcomingChargesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        // Fecha de referencia ("hoy") calculada en el servidor, en la zona del producto.
        var referenceDate = ProductCalendar.Today(_timeProvider);

        var active = await _repository.ListActiveAsync(_currentUser.GetOwnerId(), cancellationToken);

        var items = active
            .OrderBy(s => s.NextBillingDate)
            .ThenBy(s => s.Name)
            .Select(s => new UpcomingChargeItem(
                s.Id,
                s.Name,
                s.Amount,
                s.Currency,
                s.BillingCycle.ToString(),
                s.NextBillingDate,
                s.GetDaysUntilCharge(referenceDate),
                s.GetAlertWindow(referenceDate).ToString()))
            .ToList();

        return new UpcomingChargesResponse(referenceDate, items);
    }
}
