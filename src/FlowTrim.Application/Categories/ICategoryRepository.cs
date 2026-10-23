using FlowTrim.Domain.Categories;

namespace FlowTrim.Application.Categories;

/// <summary>Resultado de borrar una categoría: una categoría usada por un movimiento nunca se borra (EP-12 D5).</summary>
public enum CategoryDeleteOutcome
{
    Deleted,
    NotFound,
    InUse,
}

/// <summary>Acceso a persistencia para categorías. La implementación vive en Infrastructure.</summary>
public interface ICategoryRepository
{
    /// <summary>Verdadero si el propietario tiene al menos una categoría (archivadas incluidas).</summary>
    Task<bool> AnyAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserta las categorías por defecto saltándose las que ya existan con la misma clase y nombre: dos primeras
    /// peticiones simultáneas nunca las duplican (índice único + inserción si falta).
    /// </summary>
    Task AddDefaultsAsync(IReadOnlyList<Category> defaults, CancellationToken cancellationToken);

    /// <summary>Agrega una categoría nueva y confirma los cambios.</summary>
    Task AddAsync(Category category, CancellationToken cancellationToken);

    /// <summary>Devuelve la categoría del propietario por id, o <c>null</c> si no existe.</summary>
    Task<Category?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>Categorías del propietario ordenadas por clase y nombre; filtrables por clase y sin archivadas por defecto.</summary>
    Task<IReadOnlyList<Category>> ListAsync(
        string ownerId, CategoryKind? kind, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Verdadero si el propietario ya tiene otra categoría de esa clase con ese nombre (sin distinguir mayúsculas).</summary>
    Task<bool> NameExistsAsync(
        string ownerId, CategoryKind kind, string name, Guid? excludingId, CancellationToken cancellationToken);

    /// <summary>Ids de las categorías del propietario que tienen al menos un movimiento.</summary>
    Task<IReadOnlySet<Guid>> GetUsedIdsAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Persiste los cambios de una categoría existente.</summary>
    Task UpdateAsync(Category category, CancellationToken cancellationToken);

    /// <summary>Borra la categoría si existe y ningún movimiento la usa.</summary>
    Task<CategoryDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
