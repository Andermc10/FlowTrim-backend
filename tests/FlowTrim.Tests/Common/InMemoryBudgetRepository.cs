using FlowTrim.Application.Budgets;
using FlowTrim.Domain.Budgets;

namespace FlowTrim.Tests.Common;

/// <summary>Repositorio de presupuestos en memoria para las pruebas de API (EP-15 HU-76).</summary>
public sealed class InMemoryBudgetRepository : IBudgetRepository
{
    public List<Budget> Items { get; } = [];

    public Task AddAsync(Budget budget, CancellationToken cancellationToken)
    {
        Items.Add(budget);
        return Task.CompletedTask;
    }

    public Task<Budget?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(b => b.OwnerId == ownerId && b.Id == id));

    public Task<IReadOnlyList<Budget>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Budget>>(Items.Where(b => b.OwnerId == ownerId).ToList());

    public Task<bool> ExistsAsync(string ownerId, Guid categoryId, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(b => b.OwnerId == ownerId && b.CategoryId == categoryId && b.Currency == currency));

    public Task UpdateAsync(Budget budget, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(b => b.OwnerId == ownerId && b.Id == id) > 0);
}
