using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: crear una suscripción.</summary>
public interface ICreateSubscriptionHandler
{
    Task<CreateSubscriptionResult> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken);
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

    public CreateSubscriptionHandler(ISubscriptionRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<CreateSubscriptionResult> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        // Validación de borde: presencia y parseo (lo que el dominio no expresa bien con tipos).
        if (request.Amount is null)
        {
            errors["amount"] = ["Amount is required."];
        }

        BillingCycle billingCycle = default;
        if (string.IsNullOrWhiteSpace(request.BillingCycle))
        {
            errors["billingCycle"] = ["Billing cycle is required."];
        }
        else if (!Enum.TryParse(request.BillingCycle, ignoreCase: false, out billingCycle) || !Enum.IsDefined(billingCycle))
        {
            errors["billingCycle"] = ["Billing cycle must be 'Monthly' or 'Yearly'."];
        }

        if (request.NextBillingDate is null)
        {
            errors["nextBillingDate"] = ["Next billing date is required."];
        }

        if (errors.Count > 0)
        {
            return CreateSubscriptionResult.Invalid(errors);
        }

        try
        {
            // El dominio es la autoridad de las reglas de valor (nombre, importe, moneda, categoría).
            var subscription = Subscription.Create(
                request.Name,
                request.Category,
                request.Amount!.Value,
                request.Currency,
                billingCycle,
                request.NextBillingDate!.Value,
                _timeProvider);

            await _repository.AddAsync(subscription, cancellationToken);
            return CreateSubscriptionResult.Success(SubscriptionMapper.ToResponse(subscription));
        }
        catch (DomainValidationException exception)
        {
            return CreateSubscriptionResult.Invalid(new Dictionary<string, string[]>
            {
                [exception.PropertyName] = [exception.Message]
            });
        }
    }
}
