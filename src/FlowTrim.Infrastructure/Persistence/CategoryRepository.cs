using FlowTrim.Application.Categories;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="ICategoryRepository"/>.</summary>
internal sealed class CategoryRepository : ICategoryRepository
{
    private const string UniqueNameIndex = "ux_categories_owner_id_kind_name";

    private readonly FlowTrimDbContext _dbContext;

    public CategoryRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> AnyAsync(string ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Categories.AnyAsync(c => c.OwnerId == ownerId, cancellationToken);

    public async Task AddDefaultsAsync(IReadOnlyList<Category> defaults, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING sobre el índice único (owner_id, kind, lower(name)): si otra petición del mismo
        // usuario ya los creó, estas inserciones no hacen nada en lugar de fallar o duplicar.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var category in defaults)
        {
            await _dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO categories (id, owner_id, name, kind, color, icon, is_archived, created_at, updated_at)
                VALUES ({category.Id}, {category.OwnerId}, {category.Name}, {category.Kind.ToString()}, {category.Color},
                        {category.Icon}, {category.IsArchived}, {category.CreatedAt}, {category.UpdatedAt})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        await _dbContext.Categories.AddAsync(category, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public async Task<Category?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Categories.FirstOrDefaultAsync(c => c.OwnerId == ownerId && c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAsync(
        string ownerId,
        CategoryKind? kind,
        bool includeArchived,
        CancellationToken cancellationToken) =>
        await _dbContext.Categories
            .Where(c => c.OwnerId == ownerId && (kind == null || c.Kind == kind) && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(
        string ownerId,
        CategoryKind kind,
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        // lower() en ambos lados, igual que el índice único.
        var lowered = name.ToLower();
        return await _dbContext.Categories.AnyAsync(
            c => c.OwnerId == ownerId && c.Kind == kind && c.Name.ToLower() == lowered
                 && (excludingId == null || c.Id != excludingId),
            cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetUsedIdsAsync(string ownerId, CancellationToken cancellationToken)
    {
        var ids = await _dbContext.Transactions
            .Where(t => t.OwnerId == ownerId && t.CategoryId != null)
            .Select(t => t.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task UpdateAsync(Category category, CancellationToken cancellationToken)
    {
        _dbContext.Categories.Update(category);
        await SaveAsync(cancellationToken);
    }

    public async Task<CategoryDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var category = await GetByIdAsync(ownerId, id, cancellationToken);
        if (category is null)
        {
            return CategoryDeleteOutcome.NotFound;
        }

        if (await _dbContext.Transactions.AnyAsync(t => t.OwnerId == ownerId && t.CategoryId == id, cancellationToken))
        {
            return CategoryDeleteOutcome.InUse;
        }

        _dbContext.Categories.Remove(category);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return CategoryDeleteOutcome.Deleted;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Un movimiento creado entre la comprobación y el borrado: la FK RESTRICT lo detiene.
            _dbContext.Entry(category).State = EntityState.Unchanged;
            return CategoryDeleteOutcome.InUse;
        }
    }

    // Guarda y traduce el índice único de nombre (peticiones simultáneas) al mismo 400 que la comprobación previa.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueNameIndex })
        {
            throw new DomainValidationException("name", Category.DuplicateNameMessage);
        }
    }
}
