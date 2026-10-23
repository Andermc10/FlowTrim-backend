using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Categories;

/// <summary>Caso de uso: crear una categoría.</summary>
public interface ICreateCategoryHandler
{
    Task<HandlerResult<CategoryResponse>> HandleAsync(CreateCategoryRequest request, CancellationToken cancellationToken);
}

public sealed class CreateCategoryHandler : ICreateCategoryHandler
{
    private readonly ICategoryRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public CreateCategoryHandler(ICategoryRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<CategoryResponse>> HandleAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var errors = new Dictionary<string, string[]>();
        var kind = EnumField.Parse<CategoryKind>(request.Kind, "kind", "Category kind", errors);
        await CategoryRequestValidator.CheckUniqueNameAsync(_repository, ownerId, kind, request.Name, null, errors, cancellationToken);

        if (errors.Count > 0)
        {
            return HandlerResult<CategoryResponse>.Invalid(errors);
        }

        // Nombre, color e ícono (listas fijas) son reglas del dominio → 400 vía el manejador global.
        var category = Category.Create(ownerId, request.Name, kind, request.Color, request.Icon, _timeProvider);

        await _repository.AddAsync(category, cancellationToken);
        return HandlerResult<CategoryResponse>.Success(CategoryMapper.ToResponse(category, false));
    }
}
