using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;

namespace FlowTrim.Application.Categories;

/// <summary>Caso de uso: borrar una categoría sin movimientos; si está en uso solo se puede archivar (EP-12 D5).</summary>
public interface IDeleteCategoryHandler
{
    Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteCategoryHandler : IDeleteCategoryHandler
{
    public const string InUseCode = "category_in_use";

    private readonly ICategoryRepository _repository;
    private readonly ICurrentUser _currentUser;

    public DeleteCategoryHandler(ICategoryRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken) switch
        {
            CategoryDeleteOutcome.Deleted => HandlerResult<Done>.Success(Done.Value),
            CategoryDeleteOutcome.InUse => HandlerResult<Done>.Conflict(
                InUseCode, "The category is used by transactions. Archive it instead."),
            _ => HandlerResult<Done>.NotFound(),
        };
}
