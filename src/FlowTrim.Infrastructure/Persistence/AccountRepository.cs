using FlowTrim.Application.Accounts;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="IAccountRepository"/>.</summary>
internal sealed class AccountRepository : IAccountRepository
{
    private const string UniqueNameIndex = "ux_accounts_owner_id_name";

    private readonly FlowTrimDbContext _dbContext;

    public AccountRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        await _dbContext.Accounts.AddAsync(account, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public async Task<Account?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Accounts.FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(string ownerId, bool includeArchived, CancellationToken cancellationToken) =>
        await _dbContext.Accounts
            .Where(a => a.OwnerId == ownerId && (includeArchived || !a.IsArchived))
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string ownerId, string name, Guid? excludingId, CancellationToken cancellationToken)
    {
        // lower() en ambos lados, igual que el índice único.
        var lowered = name.ToLower();
        return await _dbContext.Accounts.AnyAsync(
            a => a.OwnerId == ownerId && a.Name.ToLower() == lowered && (excludingId == null || a.Id != excludingId),
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, AccountActivity>> GetActivityAsync(
        string ownerId,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        // Misma regla que AccountBalanceCalculator.Summarize, sumada en SQL con dos agrupaciones: por cuenta origen
        // (ingreso entra; gasto y transferencia salen) y por cuenta destino de las transferencias (entra).
        var bySource = await _dbContext.Transactions
            .Where(t => t.OwnerId == ownerId && (accountId == null || t.AccountId == accountId))
            .GroupBy(t => t.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Incoming = g.Sum(t => t.Type == TransactionType.Income ? t.Amount : 0m),
                Outgoing = g.Sum(t => t.Type == TransactionType.Income ? 0m : t.Amount),
            })
            .ToListAsync(cancellationToken);

        var byDestination = await _dbContext.Transactions
            .Where(t => t.OwnerId == ownerId && t.ToAccountId != null && (accountId == null || t.ToAccountId == accountId))
            .GroupBy(t => t.ToAccountId!.Value)
            .Select(g => new { AccountId = g.Key, Incoming = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var activity = bySource.ToDictionary(
            row => row.AccountId,
            row => new AccountActivity(row.AccountId, row.Incoming, row.Outgoing));
        foreach (var row in byDestination)
        {
            activity[row.AccountId] = activity.TryGetValue(row.AccountId, out var existing)
                ? existing with { Incoming = existing.Incoming + row.Incoming }
                : new AccountActivity(row.AccountId, row.Incoming, 0m);
        }

        return activity;
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken)
    {
        _dbContext.Accounts.Update(account);
        await SaveAsync(cancellationToken);
    }

    public async Task<AccountDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        var account = await GetByIdAsync(ownerId, id, cancellationToken);
        if (account is null)
        {
            return AccountDeleteOutcome.NotFound;
        }

        if (await _dbContext.Transactions.AnyAsync(
                t => t.OwnerId == ownerId && (t.AccountId == id || t.ToAccountId == id), cancellationToken))
        {
            return AccountDeleteOutcome.HasTransactions;
        }

        _dbContext.Accounts.Remove(account);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return AccountDeleteOutcome.Deleted;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Un movimiento creado entre la comprobación y el borrado: la FK RESTRICT lo detiene.
            _dbContext.Entry(account).State = EntityState.Unchanged;
            return AccountDeleteOutcome.HasTransactions;
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
            throw new DomainValidationException("name", Account.DuplicateNameMessage);
        }
    }
}
