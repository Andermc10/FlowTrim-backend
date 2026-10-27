using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Calendar;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Implementación EF Core de <see cref="ITransactionRepository"/>.</summary>
internal sealed class TransactionRepository : ITransactionRepository
{
    // Campo de la API que corresponde a cada FK compuesta (cuenta o categoría borrada entre la validación y el guardado).
    private static readonly Dictionary<string, (string Field, string Message)> ForeignKeyFields = new()
    {
        ["fk_transactions_accounts_owner_id_account_id"] = ("accountId", "Account not found."),
        ["fk_transactions_accounts_owner_id_to_account_id"] = ("toAccountId", "Account not found."),
        ["fk_transactions_categories_owner_id_category_id"] = ("categoryId", "Category not found."),
    };

    // Espacio de nombres de la clave del bloqueo de importación, distinto del de las suscripciones.
    private const string ImportLockPrefix = "transaction-import:";

    private readonly FlowTrimDbContext _dbContext;

    public TransactionRepository(FlowTrimDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        await _dbContext.Transactions.AddAsync(transaction, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public async Task<Transaction?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Transactions.FirstOrDefaultAsync(t => t.OwnerId == ownerId && t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAsync(
        string ownerId,
        TransactionFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await Filtered(ownerId, filter)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(string ownerId, TransactionFilter filter, CancellationToken cancellationToken) =>
        await Filtered(ownerId, filter).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<CurrencyTotals>> GetTotalsAsync(
        string ownerId,
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        // La moneda es la de la cuenta; se suma agrupando en SQL. Las transferencias no son ingreso ni gasto.
        var rows = await (
                from transaction in Filtered(ownerId, filter)
                join account in _dbContext.Accounts on transaction.AccountId equals account.Id
                where transaction.Type != TransactionType.Transfer
                group transaction by account.Currency into currency
                select new
                {
                    Currency = currency.Key,
                    Income = currency.Sum(t => t.Type == TransactionType.Income ? t.Amount : 0m),
                    Expense = currency.Sum(t => t.Type == TransactionType.Expense ? t.Amount : 0m),
                })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(row => row.Currency, StringComparer.Ordinal)
            .Select(row => new CurrencyTotals(row.Currency, row.Income, row.Expense, row.Income - row.Expense))
            .ToList();
    }

    public async Task<IReadOnlyList<DailyTotals>> GetDailyTotalsAsync(
        string ownerId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        // Agrupado por día y moneda de la cuenta en SQL; las transferencias no son ingreso ni gasto.
        var rows = await (
                from transaction in _dbContext.Transactions
                join account in _dbContext.Accounts on transaction.AccountId equals account.Id
                where transaction.OwnerId == ownerId
                    && transaction.Date >= start
                    && transaction.Date <= end
                    && transaction.Type != TransactionType.Transfer
                group transaction by new { transaction.Date, account.Currency } into day
                select new
                {
                    day.Key.Date,
                    day.Key.Currency,
                    Income = day.Sum(t => t.Type == TransactionType.Income ? t.Amount : 0m),
                    Expense = day.Sum(t => t.Type == TransactionType.Expense ? t.Amount : 0m),
                })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.Currency, StringComparer.Ordinal)
            .Select(row => new DailyTotals(row.Date, row.Currency, row.Income, row.Expense))
            .ToList();
    }

    public async Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        string ownerId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        // Solo gastos; la moneda es la de la cuenta (EP-15 D2).
        var rows = await (
                from transaction in _dbContext.Transactions
                join account in _dbContext.Accounts on transaction.AccountId equals account.Id
                where transaction.OwnerId == ownerId
                    && transaction.Type == TransactionType.Expense
                    && transaction.Date >= start
                    && transaction.Date <= end
                group transaction by new { CategoryId = transaction.CategoryId!.Value, account.Currency } into category
                select new { category.Key.CategoryId, category.Key.Currency, Amount = category.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new CategoryExpense(row.CategoryId, row.Currency, row.Amount)).ToList();
    }

    public async Task<IReadOnlyList<Transaction>> ListForImportAsync(
        string ownerId, Guid accountId, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        await _dbContext.Transactions
            .Where(t => t.OwnerId == ownerId && t.AccountId == accountId && t.Date >= start && t.Date <= end)
            .ToListAsync(cancellationToken);

    public async Task AddImportedAsync(
        string ownerId,
        Guid accountId,
        DateOnly start,
        DateOnly end,
        Func<IReadOnlyList<Transaction>, IReadOnlyList<Transaction>> selectNew,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Bloqueo consultivo por propietario hasta confirmar o revertir: dos confirmaciones del mismo archivo se ejecutan
        // una tras otra y la segunda ve lo creado por la primera (todo duplicado).
        await _dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({ImportLockPrefix + ownerId}, 0))", cancellationToken);

        var existing = await ListForImportAsync(ownerId, accountId, start, end, cancellationToken);
        _dbContext.Transactions.AddRange(selectNew(existing));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        _dbContext.Transactions.Update(transaction);
        await SaveAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Transactions.Where(t => t.OwnerId == ownerId && t.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    private IQueryable<Transaction> Filtered(string ownerId, TransactionFilter filter)
    {
        var query = _dbContext.Transactions.Where(t => t.OwnerId == ownerId);
        if (filter.From is { } from)
        {
            query = query.Where(t => t.Date >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.Date <= to);
        }

        if (filter.AccountId is { } accountId)
        {
            // Una transferencia que entra a la cuenta también es un movimiento de esa cuenta.
            query = query.Where(t => t.AccountId == accountId || t.ToAccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(t => t.Type == type);
        }

        if (filter.RecurringPaymentId is { } recurringPaymentId)
        {
            query = query.Where(t => t.RecurringPaymentId == recurringPaymentId);
        }

        if (filter.Search is { } search)
        {
            // ILIKE con los comodines del usuario escapados: "50%" busca el texto literal.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(t => t.Description != null && EF.Functions.ILike(t.Description, pattern, "\\"));
        }

        return query;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // Si la cuenta o la categoría desaparece entre la validación y el guardado, la FK lo detiene: 400 en el campo.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.ForeignKeyViolation,
                                                      ConstraintName: { } constraint,
                                                  }
                                                  && ForeignKeyFields.TryGetValue(constraint, out var mapped))
        {
            throw new DomainValidationException(mapped.Field, mapped.Message);
        }
    }
}
