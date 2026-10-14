using FlowTrim.Application.Common;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: listar suscripciones paginadas.</summary>
public interface IListSubscriptionsHandler
{
    Task<PagedResponse<SubscriptionResponse>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class ListSubscriptionsHandler : IListSubscriptionsHandler
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly ISubscriptionRepository _repository;

    public ListSubscriptionsHandler(ISubscriptionRepository repository) => _repository = repository;

    public async Task<PagedResponse<SubscriptionResponse>> HandleAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Saneado de parámetros fuera de rango.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var totalCount = await _repository.CountAsync(cancellationToken);
        var subscriptions = await _repository.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);

        var items = subscriptions.Select(SubscriptionMapper.ToResponse).ToList();
        return new PagedResponse<SubscriptionResponse>(items, page, pageSize, totalCount);
    }
}
