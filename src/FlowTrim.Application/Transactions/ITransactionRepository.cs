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

    /// <summary>Persiste los cambios de un movimiento existente.</summary>
    Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>Borra el movimiento. Devuelve <c>true</c> si existía.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
