using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Goals;

/// <summary>
/// Meta de ahorro (EP-15 HU-77, D6): nombre, monto objetivo en una moneda y fecha objetivo opcional. Lo ahorrado es la
/// suma de los aportes declarados, nunca se guarda. Que la fecha objetivo sea de hoy en adelante lo valida el caso de uso.
/// </summary>
public sealed class SavingsGoal
{
    public const int NameMaxLength = 60;

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Target { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private SavingsGoal()
    {
    }

    public static SavingsGoal Create(
        string? ownerId, string? name, string? currency, decimal target, DateOnly? targetDate, TimeProvider timeProvider)
    {
        var goal = new SavingsGoal
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerIdentity.Require(ownerId),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        goal.Apply(name, currency, target, targetDate, goal.CreatedAt);
        return goal;
    }

    /// <summary>Reemplaza los campos editables con las mismas invariantes que <see cref="Create"/>.</summary>
    public void Update(string? name, string? currency, decimal target, DateOnly? targetDate, TimeProvider timeProvider) =>
        Apply(name, currency, target, targetDate, timeProvider.GetUtcNow());

    // Valida todo antes de asignar, para que un error no deje la meta a medio cambiar.
    private void Apply(string? name, string? currency, decimal target, DateOnly? targetDate, DateTimeOffset now)
    {
        var validName = name?.Trim() ?? string.Empty;
        if (validName.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        if (validName.Length > NameMaxLength)
        {
            throw new DomainValidationException("name", $"Name must be at most {NameMaxLength} characters.");
        }

        var validCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Subscription.IsValidCurrencyFormat(validCurrency))
        {
            throw new DomainValidationException("currency", "Currency must be a 3-letter ISO 4217 code.");
        }

        if (target <= 0m || !Money.HasAtMostTwoDecimals(target) || !Money.IsWithinRange(target))
        {
            throw new DomainValidationException("target", "The target must be greater than zero, with at most 2 decimals.");
        }

        Name = validName;
        Currency = validCurrency;
        Target = target;
        TargetDate = targetDate;
        UpdatedAt = now;
    }
}
