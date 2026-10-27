using FlowTrim.Domain.Calendar;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Transactions;

/// <summary>
/// Filtro del listado de movimientos (HU-66 FR-3). Fechas inclusivas; <c>AccountId</c> incluye las transferencias que
/// entran a esa cuenta; <c>Search</c> busca en la descripción sin distinguir mayúsculas.
/// </summary>
public sealed record TransactionFilter(
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    TransactionType? Type,
    string? Search,
    Guid? RecurringPaymentId = null);

/// <summary>Gasto registrado de una categoría en una moneda de cuenta (presupuestos, EP-15 HU-76).</summary>
public sealed record CategoryExpense(Guid CategoryId, string Currency, decimal Amount);

/// <summary>Totales de una moneda para todo el filtro. Las transferencias nunca cuentan como ingreso ni gasto.</summary>
public sealed record CurrencyTotals(string Currency, decimal Income, decimal Expense, decimal Net);

/// <summary>Acceso a persistencia para movimientos. La implementación vive en Infrastructure.</summary>
public interface ITransactionRepository
{
    /// <summary>Agrega un movimiento nuevo y confirma los cambios.</summary>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>Devuelve el movimiento del propietario por id, o <c>null</c> si no existe.</summary>
    Task<Transaction?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>Página del filtro ordenada por fecha descendente y luego por creación descendente.</summary>
    Task<IReadOnlyList<Transaction>> ListAsync(
        string ownerId, TransactionFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Cantidad de movimientos del filtro (metadata de paginación).</summary>
    Task<int> CountAsync(string ownerId, TransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>Ingresos, gastos y neto por moneda de la cuenta para todo el filtro, sumados en la base de datos.</summary>
    Task<IReadOnlyList<CurrencyTotals>> GetTotalsAsync(
        string ownerId, TransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Ingresos y gastos por día y moneda de la cuenta entre dos fechas incluidas, sumados en la base de datos; las
    /// transferencias no cuentan (calendario financiero, EP-14 HU-75).
    /// </summary>
    Task<IReadOnlyList<DailyTotals>> GetDailyTotalsAsync(
        string ownerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Σ gastos por categoría y moneda de la cuenta entre dos fechas incluidas, sumados en la base de datos.</summary>
    Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        string ownerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Movimientos de la cuenta (como origen) entre dos fechas incluidas: base de los duplicados de una importación.</summary>
    Task<IReadOnlyList<Transaction>> ListForImportAsync(
        string ownerId, Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Importación atómica (EP-16 HU-79): en una sola transacción y en serie con otras importaciones del mismo propietario,
    /// lee los movimientos de la cuenta en el rango, deja que <paramref name="selectNew"/> decida cuáles crear y los
    /// inserta. Si algo falla no se crea ninguno.
    /// </summary>
    Task AddImportedAsync(
        string ownerId,
        Guid accountId,
        DateOnly from,
        DateOnly to,
        Func<IReadOnlyList<Transaction>, IReadOnlyList<Transaction>> selectNew,
        CancellationToken cancellationToken);

    /// <summary>Persiste los cambios de un movimiento existente.</summary>
    Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>Borra el movimiento. Devuelve <c>true</c> si existía.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
