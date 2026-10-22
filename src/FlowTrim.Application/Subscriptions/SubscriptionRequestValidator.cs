namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Validación de borde compartida por crear y editar: presencia y parseo de lo que el dominio no expresa con tipos.
/// Las reglas de valor (nombre, importe, moneda, categoría) son del dominio.
/// </summary>
internal static class SubscriptionRequestValidator
{
    public static void RequireAmount(decimal? amount, IDictionary<string, string[]> errors)
    {
        if (amount is null)
        {
            errors["amount"] = ["Amount is required."];
        }
    }

    public const string NextBillingDateInPastMessage = "Next billing date cannot be in the past.";

    /// <summary>
    /// Fecha requerida y, si se indica <paramref name="notBefore"/> (hoy en el calendario del producto), no anterior a
    /// ella. Editar sin cambiar una fecha ya vencida no pasa límite: la suscripción vencida se puede seguir editando.
    /// </summary>
    public static void RequireNextBillingDate(DateOnly? nextBillingDate, DateOnly? notBefore, IDictionary<string, string[]> errors)
    {
        if (nextBillingDate is null)
        {
            errors["nextBillingDate"] = ["Next billing date is required."];
        }
        else if (notBefore is { } today && nextBillingDate < today)
        {
            errors["nextBillingDate"] = [NextBillingDateInPastMessage];
        }
    }
}
