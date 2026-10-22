using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: crear una suscripción.</summary>
public interface ICreateSubscriptionHandler
{
    Task<HandlerResult<SubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Valida presencia/formato de la petición en el borde, construye la entidad de dominio
/// (que aplica las invariantes de negocio) y la persiste. Las violaciones de invariante del
/// dominio se traducen a errores por campo; nunca escapan como 500.
/// </summary>
public sealed class CreateSubscriptionHandler : ICreateSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateSubscriptionHandler(
        ISubscriptionRepository repository,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<SubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        SubscriptionRequestValidator.RequireAmount(request.Amount, errors);
        var billingCycle = SubscriptionRequestValidator.ParseEnum<BillingCycle>(
            request.BillingCycle, "billingCycle", "Billing cycle", errors);
        SubscriptionRequestValidator.RequireNextBillingDate(
            request.NextBillingDate, ProductCalendar.Today(_timeProvider), errors);

        if (errors.Count > 0)
        {
            return HandlerResult<SubscriptionResponse>.Invalid(errors);
        }

        // El dominio es la autoridad de las reglas de valor (nombre, importe, moneda, categoría).
        // Si una invariante falla lanza DomainValidationException, que el manejador global traduce a 400.
        var subscription = Subscription.Create(
            _currentUser.GetOwnerId(),
            request.Name,
            request.Category,
            request.Amount!.Value,
            request.Currency,
            billingCycle,
            request.NextBillingDate!.Value,
            _timeProvider);

        await _repository.AddAsync(subscription, cancellationToken);
        return HandlerResult<SubscriptionResponse>.Success(SubscriptionMapper.ToResponse(subscription));
    }
}
