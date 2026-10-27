using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Budgets;

/// <summary>
/// Presupuesto mensual de una categoría de gastos en una moneda (EP-15 HU-76, D1): el mismo límite se repite cada mes.
/// Que la categoría sea del propietario, de gastos y no archivada lo valida el caso de uso.
/// </summary>
public sealed class Budget
{
    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal Limit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private Budget()
    {
    }

    /// <summary>Crea un presupuesto válido. Lanza <see cref="DomainValidationException"/> ante la primera invariante incumplida.</summary>
    public static Budget Create(string? ownerId, Guid categoryId, string? currency, decimal limit, TimeProvider timeProvider)
    {
        if (categoryId == Guid.Empty)
        {
            throw new DomainValidationException("categoryId", "Category is required.");
        }

        var validCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Subscription.IsValidCurrencyFormat(validCurrency))
        {
            throw new DomainValidationException("currency", "Currency must be a 3-letter ISO 4217 code.");
        }

        var now = timeProvider.GetUtcNow();
        return new Budget
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerIdentity.Require(ownerId),
            CategoryId = categoryId,
            Currency = validCurrency,
            Limit = ValidLimit(limit),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Cambia el límite mensual; la categoría y la moneda no se editan (se borra y se crea otro).</summary>
    public void ChangeLimit(decimal limit, TimeProvider timeProvider)
    {
        Limit = ValidLimit(limit);
        UpdatedAt = timeProvider.GetUtcNow();
    }

    private static decimal ValidLimit(decimal limit)
    {
        if (limit <= 0m)
        {
            throw new DomainValidationException("limit", "The monthly limit must be greater than zero.");
        }

        if (!Money.HasAtMostTwoDecimals(limit))
        {
            throw new DomainValidationException("limit", "The monthly limit must have at most 2 decimals.");
        }

        if (!Money.IsWithinRange(limit))
        {
            throw new DomainValidationException("limit", "The monthly limit is too large.");
        }

        return limit;
    }
}
