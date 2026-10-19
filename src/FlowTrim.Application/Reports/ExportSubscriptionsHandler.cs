using System.Text;
using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;

namespace FlowTrim.Application.Reports;

/// <summary>Archivo exportado: contenido, tipo y nombre sugerido.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>Caso de uso: exportar a CSV todas las suscripciones del propietario (HU-52).</summary>
public interface IExportSubscriptionsHandler
{
    Task<ExportFile> HandleAsync(CancellationToken cancellationToken);
}

public sealed class ExportSubscriptionsHandler : IExportSubscriptionsHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public ExportSubscriptionsHandler(ISubscriptionRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<ExportFile> HandleAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await _repository.ListAllAsync(_currentUser.GetOwnerId(), cancellationToken);
        var csv = SubscriptionCsvExporter.Export(subscriptions);

        // UTF-8 con BOM: Excel lo necesita para mostrar bien tildes y eñes al abrir el archivo.
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        var date = ProductCalendar.Today(_timeProvider).ToString("yyyy-MM-dd");
        return new ExportFile(content, "text/csv; charset=utf-8", $"flowtrim-subscriptions-{date}.csv");
    }
}
