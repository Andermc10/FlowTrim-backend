using FlowTrim.Application.Categories;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Tests.Common;

/// <summary>
/// Repositorio de categorías en memoria. <see cref="Transactions"/> simula la tabla de movimientos; la factoría de la
/// API le pasa la misma lista que al repositorio de cuentas.
/// </summary>
public sealed class InMemoryCategoryRepository : ICategoryRepository
{
    public List<Category> Items { get; } = [];

    public List<Transaction> Transactions { get; init; } = [];

    /// <summary>Veces que se pidieron las categorías por defecto (para comprobar que solo se crean una vez).</summary>
    public int DefaultsRequests { get; private set; }

    public Task<bool> AnyAsync(string ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(c => c.OwnerId == ownerId));

    public Task AddDefaultsAsync(IReadOnlyList<Category> defaults, CancellationToken cancellationToken)
    {
        DefaultsRequests++;
        // Mismo efecto que ON CONFLICT DO NOTHING: se saltan las que ya existen con la misma clase y nombre.
        Items.AddRange(defaults.Where(candidate => !Items.Any(existing => SameName(existing, candidate))));
        return Task.CompletedTask;
    }

    public Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        Items.Add(category);
        return Task.CompletedTask;
    }

    public Task<Category?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(c => c.OwnerId == ownerId && c.Id == id));

    public Task<IReadOnlyList<Category>> ListAsync(
        string ownerId,
        CategoryKind? kind,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Category> categories = Items
            .Where(c => c.OwnerId == ownerId && (kind is null || c.Kind == kind) && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(categories);
    }

    public Task<bool> NameExistsAsync(
        string ownerId,
        CategoryKind kind,
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(c =>
            c.OwnerId == ownerId && c.Kind == kind
            && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
            && c.Id != excludingId));

    public Task<IReadOnlySet<Guid>> GetUsedIdsAsync(string ownerId, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid> used = Transactions
            .Where(t => t.OwnerId == ownerId && t.CategoryId is not null)
            .Select(t => t.CategoryId!.Value)
            .ToHashSet();
        return Task.FromResult(used);
    }

    public Task UpdateAsync(Category category, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<CategoryDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var category = Items.FirstOrDefault(c => c.OwnerId == ownerId && c.Id == id);
        if (category is null)
        {
            return Task.FromResult(CategoryDeleteOutcome.NotFound);
        }

        if (Transactions.Any(t => t.OwnerId == ownerId && t.CategoryId == id))
        {
            return Task.FromResult(CategoryDeleteOutcome.InUse);
        }

        Items.Remove(category);
        return Task.FromResult(CategoryDeleteOutcome.Deleted);
    }

    private static bool SameName(Category a, Category b) =>
        a.OwnerId == b.OwnerId && a.Kind == b.Kind && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
