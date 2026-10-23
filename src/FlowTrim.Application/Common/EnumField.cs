namespace FlowTrim.Application.Common;

/// <summary>Validación de borde de los enums que llegan como texto (suscripciones, cuentas, categorías, movimientos).</summary>
internal static class EnumField
{
    /// <summary>
    /// Convierte el texto al enum distinguiendo mayúsculas (contrato de la API: "Monthly", no "monthly") y solo acepta
    /// valores definidos. Si falla, añade el error del campo y devuelve el valor por defecto.
    /// </summary>
    public static TEnum Parse<TEnum>(string? value, string field, string label, IDictionary<string, string[]> errors)
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
