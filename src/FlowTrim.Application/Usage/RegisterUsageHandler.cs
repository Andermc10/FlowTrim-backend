using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Usage;

namespace FlowTrim.Application.Usage;

/// <summary>Caso de uso: registrar un uso declarado para una suscripción existente.</summary>
public interface IRegisterUsageHandler
{
    Task<HandlerResult<UsageRecordResponse>> HandleAsync(Guid subscriptionId, RegisterUsageRequest request, CancellationToken cancellationToken);
}

public sealed class RegisterUsageHandler : IRegisterUsageHandler
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IUsageRecordRepository _usageRecordRepository;
    private readonly ICurrentUser _currentUser;

    public RegisterUsageHandler(
        ISubscriptionRepository subscriptionRepository,
        IUsageRecordRepository usageRecordRepository,
        ICurrentUser currentUser)
    {
        _subscriptionRepository = subscriptionRepository;
        _usageRecordRepository = usageRecordRepository;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<UsageRecordResponse>> HandleAsync(
        Guid subscriptionId,
        RegisterUsageRequest request,
        CancellationToken cancellationToken)
    {
        // Solo se puede registrar uso de una suscripción existente.
        var subscription = await _subscriptionRepository.GetByIdAsync(_currentUser.GetOwnerId(), subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return HandlerResult<UsageRecordResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        if (request.UsageDate is null)
        {
            errors["usageDate"] = ["Usage date is required."];
        }

        if (request.UsageCount is null)
        {
            errors["usageCount"] = ["Usage count is required."];
        }

        if (errors.Count > 0)
        {
            return HandlerResult<UsageRecordResponse>.Invalid(errors);
        }

        // El dominio valida las reglas de valor (conteo no negativo, longitud de notas) → 400 vía middleware.
        var record = UsageRecord.Create(subscriptionId, request.UsageDate!.Value, request.UsageCount!.Value, request.Notes);
        await _usageRecordRepository.AddAsync(record, cancellationToken);

        return HandlerResult<UsageRecordResponse>.Success(UsageRecordMapper.ToResponse(record));
    }
}
