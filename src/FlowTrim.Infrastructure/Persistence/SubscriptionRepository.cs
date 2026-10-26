using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="ISubscriptionRepository"/>.</summary>
internal sealed class SubscriptionRepository : ISubscriptionRepository
{
    // Espacio de nombres de la clave del bloqueo, para no chocar con otros usos futuros de bloqueos consultivos.
    private const string ImportLockPrefix = "subscription-import:";

    private readonly FlowTrimDbContext _dbContext;

    public SubscriptionRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        await _dbContext.Subscriptions.AddAsync(subscription, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Subscription?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions.FirstOrDefaultAsync(
            s => s.OwnerId == ownerId && s.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<Subscription>> ListAsync(
        string ownerId, SubscriptionKind? kind, int skip, int take, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions
            .Where(s => s.OwnerId == ownerId && (kind == null || s.Kind == kind))
            .OrderBy(s => s.NextBillingDate)
            .ThenBy(s => s.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(string ownerId, SubscriptionKind? kind, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions.CountAsync(
            s => s.OwnerId == ownerId && (kind == null || s.Kind == kind), cancellationToken);

    public async Task<IReadOnlyList<Subscription>> ListActiveAsync(string ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions
            .Where(s => s.OwnerId == ownerId && s.Status == SubscriptionStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Subscription>> ListAllAsync(string ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Subscriptions
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task AddImportedAsync(
        string ownerId,
        Func<IReadOnlyList<Subscription>, IReadOnlyList<Subscription>> selectNew,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Bloqueo consultivo por propietario, liberado al confirmar o revertir: dos importaciones del mismo usuario se
        // ejecutan una tras otra y la segunda ya ve lo que creó la primera (no duplica). Otros usuarios no esperan,
        // salvo una colisión del hash, que solo los pone en fila.
        await _dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({ImportLockPrefix + ownerId}, 0))", cancellationToken);

        var existing = await ListAllAsync(ownerId, cancellationToken);
        _dbContext.Subscriptions.AddRange(selectNew(existing));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordPaymentAsync(Subscription subscription, Transaction payment, CancellationToken cancellationToken)
    {
        // SaveChanges envuelve ambos cambios en una sola transacción de base de datos.
        _dbContext.Transactions.Add(payment);
        _dbContext.Subscriptions.Update(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        _dbContext.Subscriptions.Update(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.Subscriptions.FirstOrDefaultAsync(
            s => s.OwnerId == ownerId && s.Id == id,
            cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        _dbContext.Subscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
