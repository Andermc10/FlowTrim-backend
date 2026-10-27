using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Tests.Common;

/// <summary>
/// Repositorio de movimientos en memoria sobre la lista <see cref="InMemoryAccountRepository.Transactions"/>: lo que se
/// registra aquí mueve los saldos de las cuentas, como en la base de datos. Las monedas salen de esas cuentas.
/// </summary>
public sealed class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly InMemoryAccountRepository _accounts;

    public InMemoryTransactionRepository(InMemoryAccountRepository accounts) => _accounts = accounts;

    public List<Transaction> Items => _accounts.Transactions;

    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        Items.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<Transaction?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(t => t.OwnerId == ownerId && t.Id == id));

    public Task<IReadOnlyList<Transaction>> ListAsync(
        string ownerId,
        TransactionFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Transaction> page = Filtered(ownerId, filter)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult(page);
    }

    public Task<int> CountAsync(string ownerId, TransactionFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult(Filtered(ownerId, filter).Count());

    public Task<IReadOnlyList<CurrencyTotals>> GetTotalsAsync(
        string ownerId,
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CurrencyTotals> totals = Filtered(ownerId, filter)
            .Where(t => t.Type != TransactionType.Transfer)
            .GroupBy(t => _accounts.Items.Single(a => a.Id == t.AccountId).Currency)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var income = group.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
                var expense = group.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
                return new CurrencyTotals(group.Key, income, expense, income - expense);
            })
            .ToList();
        return Task.FromResult(totals);
    }

    public Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(t => t.OwnerId == ownerId && t.Id == id) > 0);

    private IEnumerable<Transaction> Filtered(string ownerId, TransactionFilter filter) =>
        Items.Where(t =>
            t.OwnerId == ownerId
            && (filter.From is null || t.Date >= filter.From)
            && (filter.To is null || t.Date <= filter.To)
            && (filter.AccountId is null || t.AccountId == filter.AccountId || t.ToAccountId == filter.AccountId)
            && (filter.CategoryId is null || t.CategoryId == filter.CategoryId)
            && (filter.Type is null || t.Type == filter.Type)
            && (filter.RecurringPaymentId is null || t.RecurringPaymentId == filter.RecurringPaymentId)
            && (filter.Search is null
                || (t.Description?.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ?? false)));
}
