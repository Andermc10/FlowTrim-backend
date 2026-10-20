using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Tests.Common;

/// <summary>Repositorio en memoria para pruebas de los casos de uso (sin base de datos).</summary>
public sealed class InMemorySubscriptionRepository : ISubscriptionRepository
{
    public List<Subscription> Items { get; } = [];
    public int SaveCount { get; private set; }

    /// <summary>Hace fallar la próxima importación antes de guardar nada (prueba de reversión).</summary>
    public bool FailNextImport { get; set; }

    private readonly SemaphoreSlim _importLock = new(1, 1);

    public Task AddAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        Items.Add(subscription);
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<Subscription?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(s => MatchesOwner(s, ownerId) && s.Id == id));

    public Task<IReadOnlyList<Subscription>> ListAsync(string ownerId, int skip, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<Subscription> page = Items
            .Where(s => MatchesOwner(s, ownerId))
            .OrderBy(s => s.NextBillingDate)
            .ThenBy(s => s.Name)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult(page);
    }

    public Task<int> CountAsync(string ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Count(s => MatchesOwner(s, ownerId)));

    public Task<IReadOnlyList<Subscription>> ListActiveAsync(string ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Subscription> active = Items
            .Where(s => MatchesOwner(s, ownerId) && s.Status == SubscriptionStatus.Active)
            .ToList();
        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<Subscription>> ListAllAsync(string ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Subscription> all = Items
            .Where(s => MatchesOwner(s, ownerId))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(all);
    }

    public async Task AddImportedAsync(
        string ownerId,
        Func<IReadOnlyList<Subscription>, IReadOnlyList<Subscription>> selectNew,
        CancellationToken cancellationToken)
    {
        // Igual que el real: todo o nada, y en serie (la lista no es segura entre hilos).
        await _importLock.WaitAsync(cancellationToken);
        try
        {
            if (FailNextImport)
            {
                FailNextImport = false;
                throw new InvalidOperationException("Simulated persistence failure.");
            }

            var created = selectNew(await ListAllAsync(ownerId, cancellationToken));
            Items.AddRange(created);
            SaveCount++;
        }
        finally
        {
            _importLock.Release();
        }
    }

    public Task UpdateAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        // La entidad ya fue mutada en memoria (misma instancia devuelta por GetByIdAsync).
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var existing = Items.FirstOrDefault(s => MatchesOwner(s, ownerId) && s.Id == id);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        Items.Remove(existing);
        return Task.FromResult(true);
    }

    // Igual que el repositorio real: solo coincide el propietario exacto, sin comodines.
    private static bool MatchesOwner(Subscription subscription, string ownerId) =>
        subscription.OwnerId == ownerId;
}
