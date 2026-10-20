using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;

namespace FlowTrim.Application.Imports;

/// <summary>
/// Caso de uso: vista previa de un CSV de suscripciones (HU-43) con duplicados del propietario (HU-44). No persiste nada.
/// </summary>
public interface IPreviewSubscriptionImportHandler
{
    Task<SubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken);
}

public sealed class PreviewSubscriptionImportHandler : IPreviewSubscriptionImportHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public PreviewSubscriptionImportHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SubscriptionImportResult> HandleAsync(string? fileName, Stream content, CancellationToken cancellationToken)
    {
        var result = await SubscriptionImportFile.ReadAsync(fileName, content, cancellationToken);
        if (result.Preview is not { } preview || preview.Summary.Valid == 0)
        {
            return result;
        }

        // Una sola lectura del propietario; la comparación se hace en memoria (como mucho 500 filas).
        var existing = await _repository.ListAllAsync(_currentUser.GetOwnerId(), cancellationToken);
        return SubscriptionImportResult.Success(SubscriptionImportDuplicates.Classify(preview, existing));
    }
}
