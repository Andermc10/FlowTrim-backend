using FlowTrim.Application.Common;
using FlowTrim.Application.Abstractions;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>Caso de uso: listar suscripciones paginadas, opcionalmente de un solo tipo (EP-14 HU-72).</summary>
public interface IListSubscriptionsHandler
{
    Task<HandlerResult<PagedResponse<SubscriptionResponse>>> HandleAsync(
        int page, int pageSize, string? kind, CancellationToken cancellationToken);
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

    public async Task<HandlerResult<PagedResponse<SubscriptionResponse>>> HandleAsync(
        int page,
        int pageSize,
        string? kind,
        CancellationToken cancellationToken)
    {
        // Un tipo desconocido es un 400 en "kind"; sin tipo se listan todos.
        var errors = new Dictionary<string, string[]>();
        SubscriptionKind? kindFilter = string.IsNullOrWhiteSpace(kind)
            ? null
            : EnumField.Parse<SubscriptionKind>(kind, "kind", "Kind", errors);
        if (errors.Count > 0)
        {
            return HandlerResult<PagedResponse<SubscriptionResponse>>.Invalid(errors);
        }

        // Saneado de parámetros fuera de rango.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var ownerId = _currentUser.GetOwnerId();
        var totalCount = await _repository.CountAsync(ownerId, kindFilter, cancellationToken);
        var subscriptions = await _repository.ListAsync(ownerId, kindFilter, (page - 1) * pageSize, pageSize, cancellationToken);

        var items = subscriptions.Select(SubscriptionMapper.ToResponse).ToList();
        return HandlerResult<PagedResponse<SubscriptionResponse>>.Success(
            new PagedResponse<SubscriptionResponse>(items, page, pageSize, totalCount));
    }
}
