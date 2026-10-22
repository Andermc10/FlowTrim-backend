namespace FlowTrim.Domain.Common;

/// <summary>
/// Reglas comunes de importes de dinero para las finanzas diarias (EP-12): como mucho 2 decimales y dentro del rango de
/// <c>numeric(18,2)</c>, para que un valor fuera de rango sea un 400 y nunca un error de la base de datos.
/// </summary>
public static class Money
{
    /// <summary>Mayor valor absoluto que cabe en <c>numeric(18,2)</c>.</summary>
    public const decimal MaxAbsoluteValue = 9_999_999_999_999_999.99m;

    /// <summary>Verdadero si el importe no tiene más de 2 decimales significativos.</summary>
    public static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    /// <summary>Verdadero si el importe cabe en la columna de la base de datos.</summary>
    public static bool IsWithinRange(decimal value) => Math.Abs(value) <= MaxAbsoluteValue;
}
