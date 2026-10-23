using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Categories;

/// <summary>
/// Caso de uso: listar las categorías del propietario. La primera vez que no tiene ninguna crea el set por defecto en
/// el idioma de la petición (HU-65 FR-2).
/// </summary>
public interface IListCategoriesHandler
{
    Task<HandlerResult<IReadOnlyList<CategoryResponse>>> HandleAsync(
        string? kind, bool includeArchived, CategoryLanguage language, CancellationToken cancellationToken);
}

public sealed class ListCategoriesHandler : IListCategoriesHandler
{
    private readonly ICategoryRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public ListCategoriesHandler(ICategoryRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<IReadOnlyList<CategoryResponse>>> HandleAsync(
        string? kind,
        bool includeArchived,
        CategoryLanguage language,
        CancellationToken cancellationToken)
    {
        CategoryKind? kindFilter = null;
        if (kind is not null)
        {
            var errors = new Dictionary<string, string[]>();
            kindFilter = EnumField.Parse<CategoryKind>(kind, "kind", "Category kind", errors);
            if (errors.Count > 0)
            {
                return HandlerResult<IReadOnlyList<CategoryResponse>>.Invalid(errors);
            }
        }

        var ownerId = _currentUser.GetOwnerId();
        if (!await _repository.AnyAsync(ownerId, cancellationToken))
        {
            await _repository.AddDefaultsAsync(DefaultCategories.Create(ownerId, language, _timeProvider), cancellationToken);
        }

        var categories = await _repository.ListAsync(ownerId, kindFilter, includeArchived, cancellationToken);
        var used = await _repository.GetUsedIdsAsync(ownerId, cancellationToken);
        IReadOnlyList<CategoryResponse> response = categories
            .Select(category => CategoryMapper.ToResponse(category, used.Contains(category.Id)))
            .ToList();
        return HandlerResult<IReadOnlyList<CategoryResponse>>.Success(response);
    }
}
