using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Categories;

/// <summary>Petición de creación de una categoría. La clase llega como texto ("Expense") para reportar valores no admitidos.</summary>
public sealed record CreateCategoryRequest(string? Name, string? Kind, string? Color, string? Icon);

/// <summary>Reemplazo completo de los campos editables de una categoría (PUT).</summary>
public sealed record UpdateCategoryRequest(string? Name, string? Kind, string? Color, string? Icon, bool? IsArchived);

/// <summary>Categoría pública. <c>HasTransactions</c> indica que no se puede borrar ni cambiar de clase.</summary>
public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Kind,
    string Color,
    string Icon,
    bool IsArchived,
    bool HasTransactions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Mapeo explícito de la entidad al DTO público.</summary>
internal static class CategoryMapper
{
    public static CategoryResponse ToResponse(Category category, bool hasTransactions) => new(
        category.Id,
        category.Name,
        category.Kind.ToString(),
        category.Color,
        category.Icon,
        category.IsArchived,
        hasTransactions,
        category.CreatedAt,
        category.UpdatedAt);
}

/// <summary>Validación de borde compartida por crear y editar una categoría.</summary>
internal static class CategoryRequestValidator
{
    /// <summary>Añade el error de nombre repetido en la misma clase; el índice único lo vuelve a comprobar ante peticiones simultáneas.</summary>
    public static async Task CheckUniqueNameAsync(
        ICategoryRepository repository,
        string ownerId,
        CategoryKind kind,
        string? name,
        Guid? excludingId,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !errors.ContainsKey("name") && !errors.ContainsKey("kind")
            && await repository.NameExistsAsync(ownerId, kind, trimmed, excludingId, cancellationToken))
        {
            errors["name"] = [Category.DuplicateNameMessage];
        }
    }
}
