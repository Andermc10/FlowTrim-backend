using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Goals;

/// <summary>
/// Aporte declarado a una meta de ahorro (EP-15 D6): positivo es un aporte, negativo un retiro. No mueve dinero entre
/// cuentas en esta fase (D9). Que la fecha sea de hoy o antes y que lo ahorrado no quede negativo lo valida el caso de uso.
/// </summary>
public sealed class GoalContribution
{
    public const int NoteMaxLength = 200;

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid GoalId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private GoalContribution()
    {
    }

    public static GoalContribution Create(
        string? ownerId, Guid goalId, decimal amount, DateOnly date, string? note, TimeProvider timeProvider)
    {
        if (amount == 0m || !Money.HasAtMostTwoDecimals(amount) || !Money.IsWithinRange(amount))
        {
            throw new DomainValidationException(
                "amount", "The amount must not be zero and must have at most 2 decimals (negative for a withdrawal).");
        }

        var validNote = note?.Trim();
        if (string.IsNullOrEmpty(validNote))
        {
            validNote = null;
        }
        else if (validNote.Length > NoteMaxLength)
        {
            throw new DomainValidationException("note", $"Note must be at most {NoteMaxLength} characters.");
        }

        return new GoalContribution
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerIdentity.Require(ownerId),
            GoalId = goalId,
            Amount = amount,
            Date = date,
            Note = validNote,
            CreatedAt = timeProvider.GetUtcNow(),
        };
    }
}
