using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Domain.Accounts;

/// <summary>
/// Cuenta donde está el dinero del usuario (HU-64). El saldo no se guarda: se calcula con el saldo inicial y los
/// movimientos (<see cref="AccountBalanceCalculator"/>). Una cuenta archivada se oculta de los selectores pero conserva
/// su historial y su saldo.
/// </summary>
public sealed class Account
{
    /// <summary>Longitud máxima del nombre.</summary>
    public const int NameMaxLength = 60;

    /// <summary>
    /// Mensaje del nombre repetido (sin distinguir mayúsculas, archivadas incluidas). Lo usan el caso de uso y el
    /// repositorio cuando el índice único detiene una petición simultánea.
    /// </summary>
    public const string DuplicateNameMessage = "An account with this name already exists.";

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public AccountType Type { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal OpeningBalance { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private Account()
    {
    }

    /// <summary>
    /// Crea una cuenta activa. El saldo inicial puede ser negativo (p. ej. una tarjeta que ya debe dinero).
    /// Lanza <see cref="DomainValidationException"/> ante la primera invariante incumplida.
    /// </summary>
    public static Account Create(
        string? ownerId,
        string? name,
        AccountType type,
        string? currency,
        decimal openingBalance,
        TimeProvider timeProvider)
    {
        var validOwnerId = OwnerIdentity.Require(ownerId);
        var (validName, validCurrency) = ValidateAndNormalize(name, type, currency, openingBalance);

        var now = timeProvider.GetUtcNow();
        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerId = validOwnerId,
            Name = validName,
            Type = type,
            Currency = validCurrency,
            OpeningBalance = openingBalance,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Actualiza los campos editables con las mismas invariantes que <see cref="Create"/>. Que la moneda no cambie
    /// cuando ya hay movimientos lo decide el caso de uso, porque necesita consultar los movimientos.
    /// </summary>
    public void Update(
        string? name,
        AccountType type,
        string? currency,
        decimal openingBalance,
        bool isArchived,
        TimeProvider timeProvider)
    {
        var (validName, validCurrency) = ValidateAndNormalize(name, type, currency, openingBalance);

        Name = validName;
        Type = type;
        Currency = validCurrency;
        OpeningBalance = openingBalance;
        IsArchived = isArchived;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Normaliza la moneda como lo hace <see cref="Create"/> (recortada y en mayúsculas).</summary>
    public static string? NormalizeCurrency(string? currency) => currency?.Trim().ToUpperInvariant();

    private static (string Name, string Currency) ValidateAndNormalize(
        string? name,
        AccountType type,
        string? currency,
        decimal openingBalance)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainValidationException("name", $"Name must be at most {NameMaxLength} characters.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException("type", "Account type is not supported.");
        }

        // Moneda requerida: a diferencia de las suscripciones no hay valor por defecto, porque fija la moneda de
        // todos los movimientos de la cuenta.
        currency = NormalizeCurrency(currency) ?? string.Empty;
        if (currency.Length == 0)
        {
            throw new DomainValidationException("currency", "Currency is required.");
        }

        if (!Subscription.IsValidCurrencyFormat(currency))
        {
            throw new DomainValidationException("currency", "Currency must be a 3-letter ISO 4217 code.");
        }

        if (!Money.HasAtMostTwoDecimals(openingBalance))
        {
            throw new DomainValidationException("openingBalance", "Opening balance must have at most 2 decimals.");
        }

        if (!Money.IsWithinRange(openingBalance))
        {
            throw new DomainValidationException("openingBalance", "Opening balance is too large.");
        }

        return (name, currency);
    }
}
