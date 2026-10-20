using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Imports;

/// <summary>
/// Clasificación de duplicados de una vista previa (HU-44, decisión D5): misma moneda y mismo nombre sin distinguir
/// mayúsculas (ordinal, sin quitar tildes). Función pura: la vista previa y la confirmación (HU-45) usan la misma.
/// </summary>
public static class SubscriptionImportDuplicates
{
    /// <summary>
    /// Marca como duplicadas las filas válidas que repiten una suscripción existente del propietario (activa o
    /// inactiva) o una fila válida anterior del archivo. Las filas inválidas no se tocan ni cuentan como anteriores.
    /// </summary>
    public static SubscriptionImportPreview Classify(SubscriptionImportPreview preview, IEnumerable<Subscription> existing)
    {
        var existingKeys = existing.Select(subscription => Key(subscription.Name, subscription.Currency))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstRowByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var rows = preview.Rows.Select(row =>
        {
            if (row.Status != ImportRowStatus.Valid)
            {
                return row;
            }

            var key = Key(row.Values!.Name, row.Values.Currency);
            if (existingKeys.Contains(key))
            {
                return row with { Status = ImportRowStatus.Duplicate, DuplicateOf = ImportDuplicateSource.Existing };
            }

            if (firstRowByKey.TryGetValue(key, out var firstRow))
            {
                return row with
                {
                    Status = ImportRowStatus.Duplicate,
                    DuplicateOf = ImportDuplicateSource.File,
                    DuplicateOfRow = firstRow,
                };
            }

            firstRowByKey[key] = row.RowNumber;
            return row;
        }).ToList();

        return preview with { Rows = rows, Summary = ImportSummary.From(rows) };
    }

    // La moneda tiene siempre 3 letras, así que el prefijo no puede confundirse con parte del nombre.
    private static string Key(string name, string currency) => $"{currency.Trim()}|{name.Trim()}";
}
