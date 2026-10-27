using FlowTrim.Domain.Budgets;

namespace FlowTrim.Application.Budgets;

/// <summary>Petición de creación de un presupuesto mensual (EP-15 HU-76).</summary>
public sealed record CreateBudgetRequest(Guid? CategoryId, string? Currency, decimal? Limit);

/// <summary>Cambio del límite mensual (la categoría y la moneda no se editan).</summary>
public sealed record UpdateBudgetRequest(decimal? Limit);

/// <summary>Presupuesto con su avance en el mes pedido. <c>State</c> como texto: OnTrack, Warning o Exceeded.</summary>
public sealed record BudgetResponse(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CategoryColor,
    string CategoryIcon,
    bool CategoryArchived,
    string Currency,
    decimal Limit,
    decimal Spent,
    decimal Remaining,
    decimal Over,
    decimal Percent,
    string State);

/// <summary>Presupuestos de un mes <c>yyyy-MM</c>.</summary>
public sealed record BudgetListResponse(string Month, IReadOnlyList<BudgetResponse> Items);

/// <summary>Acceso a persistencia de presupuestos. La implementación vive en Infrastructure.</summary>
public interface IBudgetRepository
{
    Task AddAsync(Budget budget, CancellationToken cancellationToken);

    /// <summary>El presupuesto del propietario, o <c>null</c> si no existe o es de otro usuario.</summary>
    Task<Budget?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Budget>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Verdadero si el propietario ya tiene un presupuesto para esa categoría y moneda.</summary>
    Task<bool> ExistsAsync(string ownerId, Guid categoryId, string currency, CancellationToken cancellationToken);

    Task UpdateAsync(Budget budget, CancellationToken cancellationToken);

    /// <summary>Borra el presupuesto. Devuelve <c>true</c> si existía.</summary>
    Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
