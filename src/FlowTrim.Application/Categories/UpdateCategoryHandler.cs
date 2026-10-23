using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Categories;

/// <summary>Caso de uso: editar una categoría. La clase queda fija en cuanto un movimiento la usa.</summary>
public interface IUpdateCategoryHandler
{
    Task<HandlerResult<CategoryResponse>> HandleAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken);
}

public sealed class UpdateCategoryHandler : IUpdateCategoryHandler
{
    public const string KindLockedCode = "category_kind_locked";

    private readonly ICategoryRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public UpdateCategoryHandler(ICategoryRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<CategoryResponse>> HandleAsync(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var category = await _repository.GetByIdAsync(ownerId, id, cancellationToken);
        if (category is null)
        {
            return HandlerResult<CategoryResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var kind = EnumField.Parse<CategoryKind>(request.Kind, "kind", "Category kind", errors);
        if (request.IsArchived is null)
        {
            errors["isArchived"] = ["Archived flag is required."];
        }

        await CategoryRequestValidator.CheckUniqueNameAsync(_repository, ownerId, kind, request.Name, id, errors, cancellationToken);

        if (errors.Count > 0)
        {
            return HandlerResult<CategoryResponse>.Invalid(errors);
        }

        // Cambiar la clase de una categoría usada dejaría gastos con una categoría de ingreso (o al revés).
        var hasTransactions = (await _repository.GetUsedIdsAsync(ownerId, cancellationToken)).Contains(id);
        if (hasTransactions && kind != category.Kind)
        {
            return HandlerResult<CategoryResponse>.Conflict(
                KindLockedCode, "The kind cannot change because the category has transactions.");
        }

        category.Update(request.Name, kind, request.Color, request.Icon, request.IsArchived!.Value, _timeProvider);

        await _repository.UpdateAsync(category, cancellationToken);
        return HandlerResult<CategoryResponse>.Success(CategoryMapper.ToResponse(category, hasTransactions));
    }
}
