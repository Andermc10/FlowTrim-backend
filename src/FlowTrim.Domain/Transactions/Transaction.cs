using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Transactions;

/// <summary>
/// Movimiento registrado por el usuario (HU-66): un hecho que él confirma; FlowTrim nunca lo deduce. La moneda es la
/// de la cuenta, por eso no se guarda aquí. Una transferencia es un único registro entre dos cuentas del propietario.
/// </summary>
/// <remarks>
/// La entidad valida la forma (transferencia ⇔ cuenta destino y sin categoría). Las reglas que necesitan consultar
/// otros datos (fecha ≤ hoy, cuentas y categorías del propietario y no archivadas, clase de la categoría, misma
/// moneda) las aplica el caso de uso.
/// </remarks>
public sealed class Transaction
{
    /// <summary>Longitud máxima de la descripción.</summary>
    public const int DescriptionMaxLength = 140;

    /// <summary>Longitud máxima de la nota.</summary>
    public const int NoteMaxLength = 500;

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public TransactionType Type { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? ToAccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Description { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private Transaction()
    {
    }

    /// <summary>Crea un movimiento válido. Lanza <see cref="DomainValidationException"/> ante la primera invariante incumplida.</summary>
    public static Transaction Create(
        string? ownerId,
        TransactionType type,
        Guid accountId,
        Guid? toAccountId,
        Guid? categoryId,
        decimal amount,
        DateOnly date,
        string? description,
        string? note,
        TimeProvider timeProvider)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerIdentity.Require(ownerId),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        transaction.Apply(type, accountId, toAccountId, categoryId, amount, date, description, note, transaction.CreatedAt);
        return transaction;
    }

    /// <summary>Reemplaza los campos editables con las mismas invariantes que <see cref="Create"/>.</summary>
    public void Update(
        TransactionType type,
        Guid accountId,
        Guid? toAccountId,
        Guid? categoryId,
        decimal amount,
        DateOnly date,
        string? description,
        string? note,
        TimeProvider timeProvider) =>
        Apply(type, accountId, toAccountId, categoryId, amount, date, description, note, timeProvider.GetUtcNow());

    /// <summary>Verdadero si el movimiento mueve dinero de o hacia la cuenta indicada.</summary>
    public bool Touches(Guid accountId) => AccountId == accountId || ToAccountId == accountId;

    // Valida todo antes de asignar, para que un error no deje la entidad a medio cambiar.
    private void Apply(
        TransactionType type,
        Guid accountId,
        Guid? toAccountId,
        Guid? categoryId,
        decimal amount,
        DateOnly date,
        string? description,
        string? note,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException("type", "Transaction type is not supported.");
        }

        if (accountId == Guid.Empty)
        {
            throw new DomainValidationException("accountId", "Account is required.");
        }

        ValidateShape(type, accountId, toAccountId, categoryId);

        if (amount <= 0m)
        {
            throw new DomainValidationException("amount", "Amount must be greater than zero.");
        }

        if (!Money.HasAtMostTwoDecimals(amount))
        {
            throw new DomainValidationException("amount", "Amount must have at most 2 decimals.");
        }

        if (!Money.IsWithinRange(amount))
        {
            throw new DomainValidationException("amount", "Amount is too large.");
        }

        var validDescription = OptionalText(description, DescriptionMaxLength, "description", "Description");
        var validNote = OptionalText(note, NoteMaxLength, "note", "Note");

        Type = type;
        AccountId = accountId;
        ToAccountId = type == TransactionType.Transfer ? toAccountId : null;
        CategoryId = type == TransactionType.Transfer ? null : categoryId;
        Amount = amount;
        Date = date;
        Description = validDescription;
        Note = validNote;
        UpdatedAt = now;
    }

    // Transferencia ⇔ cuenta destino distinta y sin categoría; ingreso o gasto ⇔ categoría y sin cuenta destino.
    private static void ValidateShape(TransactionType type, Guid accountId, Guid? toAccountId, Guid? categoryId)
    {
        if (type == TransactionType.Transfer)
        {
            if (toAccountId is null || toAccountId == Guid.Empty)
            {
                throw new DomainValidationException("toAccountId", "Destination account is required for a transfer.");
            }

            if (toAccountId == accountId)
            {
                throw new DomainValidationException("toAccountId", "Destination account must be different from the source account.");
            }

            if (categoryId is not null)
            {
                throw new DomainValidationException("categoryId", "A transfer has no category.");
            }

            return;
        }

        if (categoryId is null || categoryId == Guid.Empty)
        {
            throw new DomainValidationException("categoryId", "Category is required.");
        }

        if (toAccountId is not null)
        {
            throw new DomainValidationException("toAccountId", "Only a transfer has a destination account.");
        }
    }

    // Texto opcional: recortado, vacío = ausente y dentro del límite.
    private static string? OptionalText(string? value, int maxLength, string field, string label)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            throw new DomainValidationException(field, $"{label} must be at most {maxLength} characters.");
        }

        return value;
    }
}
