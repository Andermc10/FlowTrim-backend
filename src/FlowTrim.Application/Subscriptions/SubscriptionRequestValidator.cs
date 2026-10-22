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

    public static void RequireNextBillingDate(DateOnly? nextBillingDate, IDictionary<string, string[]> errors)
    {
        if (nextBillingDate is null)
        {
            errors["nextBillingDate"] = ["Next billing date is required."];
        }
    }

    /// <summary>
    /// Convierte el texto al enum distinguiendo mayúsculas (contrato de la API: "Monthly", no "monthly") y solo acepta
    /// valores definidos. Si falla, añade el error del campo y devuelve el valor por defecto.
    /// </summary>
    public static TEnum ParseEnum<TEnum>(string? value, string field, string label, IDictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{label} is required."];
            return default;
        }

        if (Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        var allowed = string.Join(" or ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"));
        errors[field] = [$"{label} must be {allowed}."];
        return default;
    }
}
