using FlowTrim.Application.Accounts;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Tests.Common;

/// <summary>
/// Repositorio de cuentas en memoria. <see cref="Transactions"/> simula la tabla de movimientos: la actividad se calcula
/// con <see cref="AccountBalanceCalculator.Summarize"/>, la misma regla que el SQL del repositorio real.
/// </summary>
public sealed class InMemoryAccountRepository : IAccountRepository
{
    public List<Account> Items { get; } = [];

    public List<Transaction> Transactions { get; } = [];

    public Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        Items.Add(account);
        return Task.CompletedTask;
    }

    public Task<Account?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(a => a.OwnerId == ownerId && a.Id == id));

    public Task<IReadOnlyList<Account>> ListAsync(string ownerId, bool includeArchived, CancellationToken cancellationToken)
    {
        IReadOnlyList<Account> accounts = Items
            .Where(a => a.OwnerId == ownerId && (includeArchived || !a.IsArchived))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ThenBy(a => a.Id)
            .ToList();
        return Task.FromResult(accounts);
    }

    public Task<bool> NameExistsAsync(string ownerId, string name, Guid? excludingId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(a =>
            a.OwnerId == ownerId
            && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
            && a.Id != excludingId));

    public Task<IReadOnlyDictionary<Guid, AccountActivity>> GetActivityAsync(
        string ownerId,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        var activity = AccountBalanceCalculator.Summarize(Transactions.Where(t => t.OwnerId == ownerId));
        IReadOnlyDictionary<Guid, AccountActivity> result = accountId is null
            ? activity
            : activity.Where(pair => pair.Key == accountId).ToDictionary(pair => pair.Key, pair => pair.Value);
        return Task.FromResult(result);
    }

    public Task UpdateAsync(Account account, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AccountDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var account = Items.FirstOrDefault(a => a.OwnerId == ownerId && a.Id == id);
        if (account is null)
        {
            return Task.FromResult(AccountDeleteOutcome.NotFound);
        }

        if (Transactions.Any(t => t.OwnerId == ownerId && t.Touches(id)))
        {
            return Task.FromResult(AccountDeleteOutcome.HasTransactions);
        }

        Items.Remove(account);
        return Task.FromResult(AccountDeleteOutcome.Deleted);
    }
}
