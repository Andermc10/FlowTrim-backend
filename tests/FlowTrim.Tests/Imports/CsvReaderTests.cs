using FlowTrim.Application.Imports;

namespace FlowTrim.Tests.Imports;

/// <summary>HU-43: lector CSV RFC 4180 (comillas, saltos de línea, numeración como en una hoja de cálculo).</summary>
public sealed class CsvReaderTests
{
    [Fact]
    public void Read_QuotedCellsWithCommasQuotesAndNewLines_ParsesThem()
    {
        var records = CsvReader.Read("name,notes\r\n\"Netflix, HD\",\"He said \"\"hi\"\"\nsecond line\"\r\nSpotify,plain\r\n");

        Assert.Equal(3, records.Count);
        Assert.Equal(["Netflix, HD", "He said \"hi\"\nsecond line"], records[1].Cells);
        // Un registro con un salto interno ocupa una sola fila de la hoja.
        Assert.Equal((2, 3), (records[1].RowNumber, records[2].RowNumber));
        Assert.Equal(["Spotify", "plain"], records[2].Cells);
    }

    [Fact]
    public void Read_LfAndNoFinalNewline_ParsesThem()
    {
        var records = CsvReader.Read("a,b\n1,2\n3,");

        Assert.Equal([["a", "b"], ["1", "2"], ["3", ""]], records.Select(record => record.Cells));
    }

    [Fact]
    public void Read_BlankLines_AreBlankRecordsThatKeepRowNumbers()
    {
        var records = CsvReader.Read("a\r\n\r\nb\r\n");

        Assert.True(records[1].IsBlank);
        Assert.Equal(3, records[2].RowNumber);
    }

    [Fact]
    public void Read_UnterminatedQuote_ReportsTheRowWhereItStarted()
    {
        var exception = Assert.Throws<CsvFormatException>(() => CsvReader.Read("a\r\nok\r\n\"broken,\r\nmore"));

        Assert.Equal(3, exception.RowNumber);
    }

    [Fact]
    public void Read_EmptyText_HasNoRecords()
    {
        Assert.Empty(CsvReader.Read(string.Empty));
    }
}
