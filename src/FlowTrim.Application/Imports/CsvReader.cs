using System.Text;

namespace FlowTrim.Application.Imports;

/// <summary>Registro CSV con la fila que ocupa en una hoja de cálculo (la primera fila es 1).</summary>
public sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Cells)
{
    /// <summary>Línea en blanco: una sola celda vacía. Se omite, pero cuenta como fila de la hoja.</summary>
    public bool IsBlank => Cells is [""];
}

/// <summary>CSV mal formado (comillas sin cerrar). La fila indica dónde empezó la celda.</summary>
public sealed class CsvFormatException(int rowNumber)
    : Exception($"A quoted cell that starts on row {rowNumber} is never closed.")
{
    public int RowNumber { get; } = rowNumber;
}

/// <summary>
/// Lector CSV RFC 4180 mínimo (HU-43): separador coma, comillas dobles (las comillas se duplican dentro de una
/// celda citada), saltos de línea dentro de comillas, CRLF o LF y salto final opcional. Sin dependencias, igual que
/// el exportador. Las filas se numeran como en una hoja de cálculo: un registro con saltos internos ocupa una fila.
/// </summary>
public static class CsvReader
{
    public static IReadOnlyList<CsvRecord> Read(string text)
    {
        var records = new List<CsvRecord>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var quotedCellRow = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character != '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    // Comilla escapada ("") dentro de una celda citada.
                    cell.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    quotedCellRow = records.Count + 1;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r' when index + 1 < text.Length && text[index + 1] == '\n':
                    // CRLF: el salto se procesa con el \n.
                    break;
                case '\n':
                case '\r':
                    EndRecord(records, cells, cell);
                    break;
                default:
                    cell.Append(character);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new CsvFormatException(quotedCellRow);
        }

        // Último registro sin salto final (un archivo vacío no produce registros).
        if (cell.Length > 0 || cells.Count > 0)
        {
            EndRecord(records, cells, cell);
        }

        return records;
    }

    private static void EndRecord(List<CsvRecord> records, List<string> cells, StringBuilder cell)
    {
        cells.Add(cell.ToString());
        cell.Clear();
        records.Add(new CsvRecord(records.Count + 1, cells.ToArray()));
        cells.Clear();
    }
}
