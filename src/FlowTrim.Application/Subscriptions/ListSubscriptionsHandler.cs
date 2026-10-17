using FlowTrim.Application.Common;
using FlowTrim.Application.Abstractions;

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
    private readonly ICurrentUser _currentUser;

    public ListSubscriptionsHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<PagedResponse<SubscriptionResponse>> HandleAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Saneado de parámetros fuera de rango.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var ownerId = _currentUser.GetOwnerId();
        var totalCount = await _repository.CountAsync(ownerId, cancellationToken);
        var subscriptions = await _repository.ListAsync(ownerId, (page - 1) * pageSize, pageSize, cancellationToken);

        var items = subscriptions.Select(SubscriptionMapper.ToResponse).ToList();
        return new PagedResponse<SubscriptionResponse>(items, page, pageSize, totalCount);
    }
}
