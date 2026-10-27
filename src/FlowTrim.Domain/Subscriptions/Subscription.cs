using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Subscriptions;

/// <summary>
/// Pago recurrente registrado por el usuario (suscripción o factura, EP-14). La clase, la API y la tabla conservan el
/// nombre "subscription" (EP-14 D1). Protege sus invariantes mediante el
/// factory <see cref="Create"/>; no expone setters públicos que puedan saltárselas.
/// </summary>
public sealed class Subscription
{
    /// <summary>Longitud máxima del nombre.</summary>
    public const int NameMaxLength = 120;

    /// <summary>Longitud máxima de la categoría (cuando se informa).</summary>
    public const int CategoryMaxLength = 60;

    /// <summary>Longitud máxima del subject externo usado como propietario.</summary>
    public const int OwnerIdMaxLength = 200;

    /// <summary>Moneda por defecto del MVP (ISO 4217).</summary>
    public const string DefaultCurrency = "COP";

    /// <summary>Longitud máxima del enlace de pago (EP-14 D9).</summary>
    public const int PaymentUrlMaxLength = 500;

    /// <summary>Número máximo de cuotas de un plan (EP-14 D8).</summary>
    public const int MaxInstalments = 600;

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Category { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public BillingCycle BillingCycle { get; private set; }
    public DateOnly NextBillingDate { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public SubscriptionKind Kind { get; private set; }
    public string? PaymentUrl { get; private set; }
    public Guid? DefaultAccountId { get; private set; }
    public Guid? CategoryId { get; private set; }

    /// <summary>Cuotas del plan (solo <see cref="SubscriptionKind.Instalment"/>; nulo en los demás).</summary>
    public int? TotalInstalments { get; private set; }

    /// <summary>Cuotas ya pagadas: cada "marcar como pagado" suma una (0 fuera de los planes de cuotas).</summary>
    public int PaidInstalments { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core para materializar la entidad.
    private Subscription()
    {
    }

    private Subscription(
        Guid id,
        string ownerId,
        string name,
        string? category,
        decimal amount,
        string currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        SubscriptionStatus status,
        RecurringPaymentDetails details,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        Category = category;
        Amount = amount;
        Currency = currency;
        BillingCycle = billingCycle;
        NextBillingDate = nextBillingDate;
        Status = status;
        ApplyDetails(details);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Crea una suscripción válida en estado <see cref="SubscriptionStatus.Active"/> asociada al
    /// subject estable del usuario autenticado (<paramref name="ownerId"/>).
    /// Valida todas las invariantes y lanza <see cref="DomainValidationException"/> ante la primera
    /// que falle. El reloj se inyecta vía <paramref name="timeProvider"/> para que las marcas de
    /// tiempo sean deterministas en pruebas (nunca se usa DateTime.UtcNow directamente). Sin
    /// <paramref name="details"/> se crea una suscripción sin enlace ni valores por defecto.
    /// </summary>
    public static Subscription Create(
        string? ownerId,
        string? name,
        string? category,
        decimal amount,
        string? currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        TimeProvider timeProvider,
        RecurringPaymentDetails? details = null)
    {
        // Una suscripción sin propietario no puede existir: el aislamiento por usuario depende de ello.
        var validOwnerId = ownerId?.Trim() ?? string.Empty;
        if (validOwnerId.Length == 0)
        {
            throw new DomainValidationException("ownerId", "Authenticated owner is required.");
        }

        if (validOwnerId.Length > OwnerIdMaxLength)
        {
            throw new DomainValidationException("ownerId", $"Owner identifier must be at most {OwnerIdMaxLength} characters.");
        }

        var (validName, validCategory, validAmount, validCurrency) =
            ValidateAndNormalize(name, category, amount, currency, billingCycle);
        var validDetails = ValidateDetails(details ?? RecurringPaymentDetails.Default, currentPaid: null);

        var now = timeProvider.GetUtcNow();
        return new Subscription(
            Guid.NewGuid(),
            validOwnerId,
            validName,
            validCategory,
            validAmount,
            validCurrency,
            billingCycle,
            nextBillingDate,
            SubscriptionStatus.Active,
            validDetails,
            now,
            now);
    }

    /// <summary>
    /// Actualiza los campos editables aplicando las mismas invariantes que <see cref="Create"/> y
    /// refresca <see cref="UpdatedAt"/>. <see cref="Id"/> y <see cref="CreatedAt"/> no cambian. Sin
    /// <paramref name="details"/> se conservan el tipo, el enlace y los valores por defecto actuales.
    /// </summary>
    public void Update(
        string? name,
        string? category,
        decimal amount,
        string? currency,
        BillingCycle billingCycle,
        DateOnly nextBillingDate,
        SubscriptionStatus status,
        TimeProvider timeProvider,
        RecurringPaymentDetails? details = null)
    {
        var (validName, validCategory, validAmount, validCurrency) =
            ValidateAndNormalize(name, category, amount, currency, billingCycle);

        if (!Enum.IsDefined(status))
        {
            throw new DomainValidationException("status", "Status is not supported.");
        }

        var validDetails = details is null ? null : ValidateDetails(details, PaidInstalments);
        var (kind, total, paid) = validDetails is null
            ? (Kind, TotalInstalments, PaidInstalments)
            : (validDetails.Kind, validDetails.TotalInstalments, validDetails.PaidInstalments ?? 0);

        // Un plan con todas sus cuotas pagadas no vuelve a cobrar: reactivarlo exige ampliar el número de cuotas.
        if (kind == SubscriptionKind.Instalment && status == SubscriptionStatus.Active && paid >= total)
        {
            throw new DomainValidationException(
                "status", "This instalment plan is completed. Raise the number of instalments to reactivate it.");
        }

        Name = validName;
        Category = validCategory;
        Amount = validAmount;
        Currency = validCurrency;
        BillingCycle = billingCycle;
        NextBillingDate = nextBillingDate;
        Status = status;
        if (validDetails is not null)
        {
            ApplyDetails(validDetails);
        }

        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Marca el cobro actual como pagado: la próxima fecha avanza exactamente un ciclo desde la actual con la regla de
    /// <see cref="RecurrenceSchedule.Next"/> (nunca desde hoy: los ciclos saltados siguen vencidos). No registra ningún
    /// pago (HU-57). En un plan de cuotas suma una cuota pagada y, con la última, el plan queda completado: pasa a
    /// <see cref="SubscriptionStatus.Inactive"/> y deja de contar en todos los cobros esperados (HU-74).
    /// </summary>
    public void AdvanceBillingDate(TimeProvider timeProvider)
    {
        if (Status != SubscriptionStatus.Active)
        {
            throw new DomainValidationException("status", "Only active subscriptions can be marked as paid.");
        }

        NextBillingDate = RecurrenceSchedule.Next(NextBillingDate, BillingCycle);
        if (Kind == SubscriptionKind.Instalment)
        {
            PaidInstalments++;
            if (IsCompleted)
            {
                Status = SubscriptionStatus.Inactive;
            }
        }

        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Verdadero si es un plan de cuotas con todas sus cuotas pagadas.</summary>
    public bool IsCompleted => Kind == SubscriptionKind.Instalment && PaidInstalments >= TotalInstalments;

    /// <summary>Cuotas que faltan por pagar en un plan de cuotas; nulo en los demás pagos.</summary>
    public int? RemainingInstalments => Kind == SubscriptionKind.Instalment ? TotalInstalments - PaidInstalments : null;

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
    /// Costo mensual equivalente según el ciclo de cobro (<see cref="RecurrenceSchedule.MonthlyEquivalent"/>, redondeado
    /// a 2 decimales). No suma ni convierte monedas; opera sobre un único importe.
    /// </summary>
    public decimal GetMonthlyEquivalent() => RecurrenceSchedule.MonthlyEquivalent(Amount, BillingCycle);

    /// <summary>
    /// Cobros esperados entre dos fechas incluidas (<see cref="RecurrenceSchedule.Occurrences"/>); un plan de cuotas
    /// nunca proyecta más que sus cuotas restantes (HU-74).
    /// </summary>
    public IReadOnlyList<DateOnly> GetOccurrences(DateOnly from, DateOnly to) =>
        RecurrenceSchedule.Occurrences(NextBillingDate, BillingCycle, from, to, RemainingInstalments);

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

    /// <summary>
    /// Enlace de pago válido (EP-14 D9): URL absoluta <c>https://</c> con servidor, hasta
    /// <see cref="PaymentUrlMaxLength"/> caracteres y sin usuario ni contraseña. Pública para que la importación CSV use
    /// la misma regla.
    /// </summary>
    public static bool IsValidPaymentUrl(string url) =>
        url.Length <= PaymentUrlMaxLength
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;

    // Valida y normaliza los datos de pago recurrente: tipo definido, enlace https (vacío cuenta como ausente) y, en un
    // plan de cuotas, total entre 1 y 600 y cuotas pagadas válidas. Al crear (currentPaid nulo) las pagadas pueden venir
    // en los datos (plan ya empezado, menos que el total); al editar se conservan las que ya lleva el plan.
    private static RecurringPaymentDetails ValidateDetails(RecurringPaymentDetails details, int? currentPaid)
    {
        if (!Enum.IsDefined(details.Kind))
        {
            throw new DomainValidationException("kind", "Kind is not supported.");
        }

        var paymentUrl = details.PaymentUrl?.Trim();
        if (string.IsNullOrEmpty(paymentUrl))
        {
            paymentUrl = null;
        }
        else if (!IsValidPaymentUrl(paymentUrl))
        {
            throw new DomainValidationException("paymentUrl", PaymentUrlMessage);
        }

        if (details.Kind != SubscriptionKind.Instalment)
        {
            return details with { PaymentUrl = paymentUrl, TotalInstalments = null, PaidInstalments = 0 };
        }

        if (details.TotalInstalments is not { } total || total < 1 || total > MaxInstalments)
        {
            throw new DomainValidationException(
                "totalInstalments", $"An instalment plan needs between 1 and {MaxInstalments} instalments.");
        }

        var paid = currentPaid ?? details.PaidInstalments ?? 0;
        if (currentPaid is null && (paid < 0 || paid >= total))
        {
            throw new DomainValidationException(
                "paidInstalments", "Instalments already paid must be zero or more and fewer than the total.");
        }

        if (currentPaid is not null && total < paid)
        {
            throw new DomainValidationException(
                "totalInstalments", $"The plan already has {paid} paid instalments; the total cannot be lower.");
        }

        return details with { PaymentUrl = paymentUrl, TotalInstalments = total, PaidInstalments = paid };
    }

    /// <summary>Mensaje del enlace de pago inválido, compartido con la importación CSV.</summary>
    public const string PaymentUrlMessage =
        "Payment link must be an https:// address of at most 500 characters without a user name or password.";

    private void ApplyDetails(RecurringPaymentDetails details)
    {
        Kind = details.Kind;
        PaymentUrl = details.PaymentUrl;
        DefaultAccountId = details.DefaultAccountId;
        CategoryId = details.CategoryId;
        TotalInstalments = details.TotalInstalments;
        PaidInstalments = details.PaidInstalments ?? 0;
    }

    /// <summary>
    /// Valida que la moneda (ya en mayúsculas) tenga exactamente 3 letras A-Z: solo formato, sin lista ISO real.
    /// Pública para que la importación CSV (HU-43) use la misma regla.
    /// </summary>
    public static bool IsValidCurrencyFormat(string currency)
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
