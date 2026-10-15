namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: listar los próximos cobros de las suscripciones activas con su cuenta regresiva.</summary>
public interface IGetUpcomingChargesHandler
{
    Task<UpcomingChargesResponse> HandleAsync(CancellationToken cancellationToken);
}

public sealed class GetUpcomingChargesHandler : IGetUpcomingChargesHandler
{
    // Colombia (America/Bogota) no tiene horario de verano, así que un offset fijo UTC-5 es exacto
    // y evita depender de la base de datos de zonas horarias del sistema.
    private static readonly TimeSpan ReferenceOffset = TimeSpan.FromHours(-5);

    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public GetUpcomingChargesHandler(ISubscriptionRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<UpcomingChargesResponse> HandleAsync(CancellationToken cancellationToken)
    {
        // Fecha de referencia ("hoy") calculada en el servidor, en la zona del producto.
        var referenceDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ReferenceOffset).DateTime);

        var active = await _repository.ListActiveAsync(cancellationToken);

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
