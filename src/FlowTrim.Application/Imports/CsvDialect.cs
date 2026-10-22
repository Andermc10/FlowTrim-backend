namespace FlowTrim.Application.Imports;

/// <summary>
/// Variante del CSV según la hoja de cálculo que lo guardó (HU-58). Excel con configuración regional de Colombia separa
/// columnas con <c>;</c> y escribe decimales con coma y fechas <c>dd/MM/yyyy</c>; esos archivos se aceptan tal cual.
/// </summary>
public sealed record CsvDialect(char Delimiter)
{
    /// <summary>Prefijo de la primera línea opcional que indica el separador a Excel (<c>sep=,</c>).</summary>
    public const string SeparatorHintPrefix = "sep=";

    public static readonly CsvDialect Comma = new(',');
    public static readonly CsvDialect Semicolon = new(';');

    /// <summary>En archivos con <c>;</c> la coma es decimal y las fechas pueden venir como día/mes/año.</summary>
    public bool IsSpreadsheetLocale => Delimiter == ';';

    /// <summary>Formatos de fecha aceptados: siempre <c>yyyy-MM-dd</c>; con <c>;</c> también día/mes/año.</summary>
    public IReadOnlyList<string> DateFormats => IsSpreadsheetLocale
        ? ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"]
        : ["yyyy-MM-dd"];

    /// <summary>
    /// Detecta el separador. Una primera línea <c>sep=X</c> lo fija y se quita del contenido (Excel tampoco la muestra
    /// como fila, así que la cabecera sigue siendo la fila 1). Sin ella, una cabecera con <c>;</c> y sin comas usa
    /// <c>;</c>. Devuelve <c>null</c> si la línea <c>sep=</c> pide un separador que no se admite.
    /// </summary>
    public static (CsvDialect? Dialect, string Content) Detect(string text)
    {
        var lineEnd = text.IndexOf('\n');
        var firstLine = (lineEnd < 0 ? text : text[..lineEnd]).TrimEnd('\r');

        if (firstLine.StartsWith(SeparatorHintPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var dialect = firstLine[SeparatorHintPrefix.Length..] switch
            {
                "," => Comma,
                ";" => Semicolon,
                _ => null,
            };
            return (dialect, lineEnd < 0 ? string.Empty : text[(lineEnd + 1)..]);
        }

        return (firstLine.Contains(';') && !firstLine.Contains(',') ? Semicolon : Comma, text);
    }
}
