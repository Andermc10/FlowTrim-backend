using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Suscripción recurrente registrada por el usuario. Protege sus invariantes mediante el
/// factory <see cref="Create"/>; no expone setters públicos que puedan saltárselas.
/// </summary>
public sealed class Subscription
{
    /// <summary>Longitud máxima del nombre.</summary>
    public const int NameMaxLength = 120;

    /// <summary>Longitud máxima de la categoría (cuando se informa).</summary>
    public const int CategoryMaxLength = 60;

    /// <summary>Moneda por defecto del MVP (ISO 4217).</summary>
    public const string DefaultCurrency = "COP";

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Category { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public BillingCycle BillingCycle { get; private set; }
    public DateOnly NextBillingDate { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core para materializar la entidad.
    private Subscription()
    {
    }

    private Subscription(
        Guid id,
        string name,
        string? category,
        decimal amount,
        string currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        SubscriptionStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        Category = category;
        Amount = amount;
        Currency = currency;
        BillingCycle = billingCycle;
        NextBillingDate = nextBillingDate;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Crea una suscripción válida en estado <see cref="SubscriptionStatus.Active"/>.
    /// Valida todas las invariantes y lanza <see cref="DomainValidationException"/> ante la primera
    /// que falle. El reloj se inyecta vía <paramref name="timeProvider"/> para que las marcas de
    /// tiempo sean deterministas en pruebas (nunca se usa DateTime.UtcNow directamente).
    /// </summary>
    public static Subscription Create(
        string? name,
        string? category,
        decimal amount,
        string? currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        TimeProvider timeProvider)
    {
        var (validName, validCategory, validAmount, validCurrency) =
            ValidateAndNormalize(name, category, amount, currency, billingCycle);

        var now = timeProvider.GetUtcNow();
        return new Subscription(
            Guid.NewGuid(),
            validName,
            validCategory,
            validAmount,
            validCurrency,
            billingCycle,
            nextBillingDate,
            SubscriptionStatus.Active,
            now,
            now);
    }

    /// <summary>
    /// Actualiza los campos editables aplicando las mismas invariantes que <see cref="Create"/> y
    /// refresca <see cref="UpdatedAt"/>. <see cref="Id"/> y <see cref="CreatedAt"/> no cambian.
    /// </summary>
    public void Update(
        string? name,
        string? category,
        decimal amount,
        string? currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        SubscriptionStatus status,
        TimeProvider timeProvider)
    {
        var (validName, validCategory, validAmount, validCurrency) =
            ValidateAndNormalize(name, category, amount, currency, billingCycle);

        if (!Enum.IsDefined(status))
        {
            throw new DomainValidationException("status", "Status is not supported.");
        }

        Name = validName;
        Category = validCategory;
        Amount = validAmount;
        Currency = validCurrency;
        BillingCycle = billingCycle;
        NextBillingDate = nextBillingDate;
        Status = status;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    // Valida y normaliza los campos comunes a creación y actualización. Lanza ante la primera invariante
    // incumplida. Devuelve los valores ya normalizados (nombre recortado, importe redondeado, moneda en mayúsculas).
    private static (string Name, string? Category, decimal Amount, string Currency) ValidateAndNormalize(
        string? name,
        string? category,
        decimal amount,
        string? currency,
        BillingCycle billingCycle)
    {
        // Nombre: requerido, recortado, no vacío y dentro del límite.
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainValidationException("name", $"Name must be at most {NameMaxLength} characters.");
        }

        // Importe: redondeado a 2 decimales y estrictamente mayor que cero (sobre el valor redondeado).
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
        {
            throw new DomainValidationException("amount", "Amount must be greater than zero.");
        }

        // Moneda: por defecto COP; se normaliza a mayúsculas y se valida el formato ISO 4217 (3 letras).
        currency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCurrency
            : currency.Trim().ToUpperInvariant();
        if (!IsValidCurrencyFormat(currency))
        {
            throw new DomainValidationException("currency", "Currency must be a 3-letter ISO 4217 code.");
        }

        // Categoría: opcional; cadena vacía se trata como ausente.
        if (category is not null)
        {
            category = category.Trim();
            if (category.Length == 0)
            {
                category = null;
            }
            else if (category.Length > CategoryMaxLength)
            {
                throw new DomainValidationException("category", $"Category must be at most {CategoryMaxLength} characters.");
            }
        }

        // Ciclo de cobro: debe ser un valor definido del enum.
        if (!Enum.IsDefined(billingCycle))
        {
            throw new DomainValidationException("billingCycle", "Billing cycle is not supported.");
        }

        return (name, category, amount, currency);
    }

    /// <summary>
    /// Costo mensual equivalente según el ciclo de cobro: mensual → importe; anual → importe / 12
    /// (redondeado a 2 decimales). No suma ni convierte monedas; opera sobre un único importe.
    /// </summary>
    public decimal GetMonthlyEquivalent() => BillingCycle switch
    {
        BillingCycle.Monthly => Amount,
        BillingCycle.Yearly => Math.Round(Amount / 12m, 2, MidpointRounding.AwayFromZero),
        _ => throw new InvalidOperationException($"Unsupported billing cycle: {BillingCycle}")
    };

    /// <summary>
    /// Días enteros hasta el próximo cobro respecto a <paramref name="referenceDate"/>.
    /// Negativo si la fecha ya pasó (vencido). Función pura: la fecha de referencia se pasa como parámetro.
    /// </summary>
    public int GetDaysUntilCharge(DateOnly referenceDate) => NextBillingDate.DayNumber - referenceDate.DayNumber;

    /// <summary>Clasifica el cobro en una ventana de alerta según los días que faltan.</summary>
    public AlertWindow GetAlertWindow(DateOnly referenceDate) => GetDaysUntilCharge(referenceDate) switch
    {
        < 0 => AlertWindow.Overdue,
        0 => AlertWindow.Today,
        <= 3 => AlertWindow.DueWithin3Days,
        <= 7 => AlertWindow.DueWithin7Days,
        _ => AlertWindow.Later
    };

    // Valida que la moneda tenga exactamente 3 letras A-Z (solo formato, sin lista ISO real: MVP mono-moneda).
    private static bool IsValidCurrencyFormat(string currency)
    {
        if (currency.Length != 3)
        {
            return false;
        }

        foreach (char c in currency)
        {
            if (c is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
