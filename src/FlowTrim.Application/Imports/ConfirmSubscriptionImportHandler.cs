using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Imports;

/// <summary>Conteos de una importación confirmada (HU-45): siempre suman <c>Total</c>.</summary>
public sealed record SubscriptionImportConfirmation(int Total, int Created, int Invalid, int Duplicate);

/// <summary>Resultado de confirmar: los conteos o un error de archivo (en cuyo caso no se creó nada).</summary>
public sealed record ConfirmSubscriptionImportResult(SubscriptionImportConfirmation? Confirmation, ImportFileError? FileError);

/// <summary>
/// Caso de uso: confirmar una importación reenviando el mismo archivo (HU-45, decisión D4). Repite la validación y la
/// clasificación de duplicados dentro de la transacción y crea solo las filas que siguen siendo válidas.
/// </summary>
public interface IConfirmSubscriptionImportHandler
{
    Task<ConfirmSubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken);
}

public sealed class ConfirmSubscriptionImportHandler : IConfirmSubscriptionImportHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public ConfirmSubscriptionImportHandler(ISubscriptionRepository repository, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<ConfirmSubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken)
    {
        var result = await SubscriptionImportFile.ReadAsync(
            fileName, content, ProductCalendar.Today(_timeProvider), cancellationToken);
        if (result.Preview is not { } preview)
        {
            return new ConfirmSubscriptionImportResult(null, result.FileError);
        }

        // Sin filas válidas no hay nada que crear ni duplicados que buscar.
        var summary = preview.Summary;
        if (summary.Valid > 0)
        {
            var ownerId = _currentUser.GetOwnerId();
            await _repository.AddImportedAsync(ownerId, existing =>
            {
                // La clasificación se hace con los datos leídos dentro de la transacción, no con los de la vista previa.
                var classified = SubscriptionImportDuplicates.Classify(preview, existing);
                summary = classified.Summary;
                return classified.Rows
                    .Where(row => row.Status == ImportRowStatus.Valid)
                    .Select(row => ToSubscription(ownerId, row.Values!))
                    .ToList();
            }, cancellationToken);
        }

        return new ConfirmSubscriptionImportResult(
            new SubscriptionImportConfirmation(summary.Total, summary.Valid, summary.Invalid, summary.Duplicate), null);
    }

    // Los valores ya pasaron las reglas del dominio en la vista previa; Create las vuelve a aplicar igualmente.
    private Subscription ToSubscription(string ownerId, ImportRowValues values)
    {
        var billingCycle = Enum.Parse<BillingCycle>(values.BillingCycle);
        var details = new RecurringPaymentDetails(Enum.Parse<SubscriptionKind>(values.Kind), values.PaymentUrl, null, null);
        var subscription = Subscription.Create(ownerId, values.Name, values.Category, values.Amount, values.Currency,
            billingCycle, values.NextBillingDate, _timeProvider, details);

        // Create siempre deja la suscripción activa; una fila "Inactive" se crea inactiva desde el inicio.
        var status = Enum.Parse<SubscriptionStatus>(values.Status);
        if (status != SubscriptionStatus.Active)
        {
            subscription.Update(subscription.Name, subscription.Category, subscription.Amount, subscription.Currency,
                subscription.BillingCycle, subscription.NextBillingDate, status, _timeProvider);
        }

        return subscription;
    }
}
