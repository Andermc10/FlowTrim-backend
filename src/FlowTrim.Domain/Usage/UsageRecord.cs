using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Usage;

/// <summary>
/// Uso declarado por el usuario para una suscripción (no verificado). Protege sus invariantes
/// mediante el factory <see cref="Create"/>.
/// </summary>
public sealed class UsageRecord
{
    /// <summary>Longitud máxima de las notas (cuando se informan).</summary>
    public const int NotesMaxLength = 500;

    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public DateOnly UsageDate { get; private set; }
    public int UsageCount { get; private set; }
    public string? Notes { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private UsageRecord()
    {
    }

    private UsageRecord(Guid id, Guid subscriptionId, DateOnly usageDate, int usageCount, string? notes)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        UsageDate = usageDate;
        UsageCount = usageCount;
        Notes = notes;
    }

    /// <summary>
    /// Crea un registro de uso válido. Valida las invariantes y lanza <see cref="DomainValidationException"/>
    /// ante la primera que falle.
    /// </summary>
    public static UsageRecord Create(Guid subscriptionId, DateOnly usageDate, int usageCount, string? notes)
    {
        if (subscriptionId == Guid.Empty)
        {
            throw new DomainValidationException("subscriptionId", "Subscription is required.");
        }

        if (usageCount < 0)
        {
            throw new DomainValidationException("usageCount", "Usage count cannot be negative.");
        }

        // Notas opcionales; cadena vacía se trata como ausente.
        if (notes is not null)
        {
            notes = notes.Trim();
            if (notes.Length == 0)
            {
                notes = null;
            }
            else if (notes.Length > NotesMaxLength)
            {
                throw new DomainValidationException("notes", $"Notes must be at most {NotesMaxLength} characters.");
            }
        }

        return new UsageRecord(Guid.NewGuid(), subscriptionId, usageDate, usageCount, notes);
    }
}
