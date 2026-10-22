using System.Globalization;
using System.Text;
using FlowTrim.Application.Imports;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Reports;

/// <summary>
/// Convierte suscripciones en CSV (RFC 4180): cabecera fija, fechas ISO, decimales con punto y líneas CRLF.
/// Protege contra inyección de fórmulas: una celda de texto que empieza por = + - @ tab o CR se antepone con '.
/// </summary>
public static class SubscriptionCsvExporter
{
    public static readonly string Header = string.Join(',', SubscriptionCsvFormat.Columns);
    private static readonly char[] CharactersRequiringQuotes = [',', '"', '\r', '\n'];

    public static string Export(IEnumerable<Subscription> subscriptions)
    {
        var csv = new StringBuilder();
        // Indica el separador a Excel: sin ella, Excel en español busca ";" y muestra todo en una columna (HU-58).
        csv.Append(CsvDialect.SeparatorHintPrefix).Append(',').Append("\r\n");
        csv.Append(Header).Append("\r\n");

        foreach (var subscription in subscriptions)
        {
            csv.AppendJoin(',',
                Text(subscription.Name),
                Text(subscription.Category),
                Number(subscription.Amount),
                subscription.Currency,
                subscription.BillingCycle.ToString(),
                Number(subscription.GetMonthlyEquivalent()),
                subscription.NextBillingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                subscription.Status.ToString());
            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    // Las celdas de texto son las únicas que vienen del usuario: se neutralizan fórmulas y se citan si hace falta.
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = SubscriptionCsvFormat.FormulaPrefixes.Contains(value[0]) ? SubscriptionCsvFormat.FormulaGuard + value : value;
        return safe.IndexOfAny(CharactersRequiringQuotes) >= 0
            ? "\"" + safe.Replace("\"", "\"\"") + "\""
            : safe;
    }
}
