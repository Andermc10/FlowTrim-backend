using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: actualizar una suscripción existente.</summary>
public interface IUpdateSubscriptionHandler
{
    Task<HandlerResult<SubscriptionResponse>> HandleAsync(Guid id, UpdateSubscriptionRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateSubscriptionHandler : IUpdateSubscriptionHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateSubscriptionHandler(ISubscriptionRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<SubscriptionResponse>> HandleAsync(
        Guid id,
        UpdateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await _repository.GetByIdAsync(_currentUser.GetOwnerId(), id, cancellationToken);
        if (subscription is null)
        {
            return HandlerResult<SubscriptionResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        SubscriptionRequestValidator.RequireAmount(request.Amount, errors);
        var billingCycle = EnumField.Parse<BillingCycle>(
            request.BillingCycle, "billingCycle", "Billing cycle", errors);
        var status = EnumField.Parse<SubscriptionStatus>(request.Status, "status", "Status", errors);
        // Solo una fecha nueva debe ser de hoy en adelante (HU-56).
        var notBefore = request.NextBillingDate != subscription.NextBillingDate ? ProductCalendar.Today(_timeProvider) : (DateOnly?)null;
        SubscriptionRequestValidator.RequireNextBillingDate(request.NextBillingDate, notBefore, errors);

        if (errors.Count > 0)
        {
            return HandlerResult<SubscriptionResponse>.Invalid(errors);
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
        return HandlerResult<SubscriptionResponse>.Success(SubscriptionMapper.ToResponse(subscription));
    }
}
