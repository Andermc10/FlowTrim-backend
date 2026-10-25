namespace FlowTrim.Application.Reports;

/// <summary>
/// Formato CSV de suscripciones compartido por la exportación (HU-52) y la importación (HU-43): el mismo archivo
/// exportado se puede volver a importar. Columnas en snake_case y en este orden al exportar; <c>kind</c> y
/// <c>payment_url</c> (EP-14 HU-72) van al final para no mover las anteriores. La cuenta y la categoría por defecto no
/// forman parte del CSV.
/// </summary>
public static class SubscriptionCsvFormat
{
    public const string Name = "name";
    public const string Category = "category";
    public const string Amount = "amount";
    public const string Currency = "currency";
    public const string BillingCycle = "billing_cycle";
    public const string MonthlyEquivalent = "monthly_equivalent";
    public const string NextBillingDate = "next_billing_date";
    public const string Status = "status";
    public const string Kind = "kind";
    public const string PaymentUrl = "payment_url";

    public static readonly IReadOnlyList<string> Columns =
        [Name, Category, Amount, Currency, BillingCycle, MonthlyEquivalent, NextBillingDate, Status, Kind, PaymentUrl];

    /// <summary>Caracteres con los que una celda podría interpretarse como fórmula en una hoja de cálculo.</summary>
    public static readonly IReadOnlyList<char> FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Marca que el exportador antepone a una celda que empieza por un prefijo de fórmula.</summary>
    public const char FormulaGuard = '\'';
}
