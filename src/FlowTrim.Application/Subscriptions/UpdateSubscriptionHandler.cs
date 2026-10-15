using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: actualizar una suscripción existente.</summary>
public interface IUpdateSubscriptionHandler
{
    Task<UpdateSubscriptionResult> HandleAsync(Guid id, UpdateSubscriptionRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateSubscriptionHandler : IUpdateSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UpdateSubscriptionHandler(ISubscriptionRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateSubscriptionResult> HandleAsync(
        Guid id,
        UpdateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetByIdAsync(id, cancellationToken);
        if (subscription is null)
        {
            return UpdateSubscriptionResult.NotFound();
        }

        var errors = new Dictionary<string, string[]>();

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

        SubscriptionStatus status = default;
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            errors["status"] = ["Status is required."];
        }
        else if (!Enum.TryParse(request.Status, ignoreCase: false, out status) || !Enum.IsDefined(status))
        {
            errors["status"] = ["Status must be 'Active' or 'Inactive'."];
        }

        if (request.NextBillingDate is null)
        {
            errors["nextBillingDate"] = ["Next billing date is required."];
        }

        if (errors.Count > 0)
        {
            return UpdateSubscriptionResult.Invalid(errors);
        }

        // Las invariantes de dominio que fallen lanzan DomainValidationException → 400 (manejador global).
        subscription.Update(
            request.Name,
            request.Category,
            request.Amount!.Value,
            request.Currency,
            billingCycle,
            request.NextBillingDate!.Value,
            status,
            _timeProvider);

        await _repository.UpdateAsync(subscription, cancellationToken);
        return UpdateSubscriptionResult.Success(SubscriptionMapper.ToResponse(subscription));
    }
}
