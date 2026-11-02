using System.Text.Json;
using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Application.Export.Services;

namespace CrmNlQuery.Tests.Application.Export.Services;

/// <summary>
/// Tests CSV and JSON Lines export formatting, including injection protection and the documented schema.
/// </summary>
public class ExportFormatterTests
{
    private static readonly ExportColumn[] Columns =
    {
        new ExportColumn("who", "text", true),
        new ExportColumn("note", "text", true),
        new ExportColumn("rows", "number", false),
    };

    /// <summary>
    /// Verifies that CSV injection is neutralized on text columns.
    /// </summary>
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("plain", "plain")]
    [InlineData("a=b", "a=b")]
    public void CsvInjection_IsNeutralized_OnTextColumns(string value, string expectedCell)
    {
        var line = ExportFormatter.Row(ExportFormat.Csv, Columns, new object?[] { value, "x", 1 });

        Assert.Equal(expectedCell + ",x,1\r\n", line);
    }

    /// <summary>
    /// Verifies that a tab or carriage return start is also neutralized.
    /// </summary>
    [Fact]
    public void ATabOrCarriageReturnStart_IsAlsoNeutralized()
    {
        Assert.StartsWith("'\tcmd", ExportFormatter.Row(ExportFormat.Csv, Columns, new object?[] { "\tcmd", "", 1 }));
    }

    /// <summary>
    /// Verifies that number columns stay numbers even when negative.
    /// </summary>
    [Fact]
    public void NumberColumns_StayNumbers_EvenWhenNegative()
    {
        var line = ExportFormatter.Row(ExportFormat.Csv, Columns, new object?[] { "a", "b", -5 });

        Assert.Equal("a,b,-5\r\n", line);
    }

    /// <summary>
    /// Verifies that commas quotes and newlines are quoted per rfc4180.
    /// </summary>
    [Fact]
    public void Commas_Quotes_AndNewlines_AreQuotedPerRfc4180()
    {
        var line = ExportFormatter.Row(ExportFormat.Csv, Columns, new object?[] { "a,b", "say \"hi\"\nnow", 7 });

        Assert.Equal("\"a,b\",\"say \"\"hi\"\"\nnow\",7\r\n", line);
    }

    /// <summary>
    /// Verifies that null cells are empty and numbers use the invariant culture.
    /// </summary>
    [Fact]
    public void NullCells_AreEmpty_AndNumbersUseTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            var line = ExportFormatter.Row(ExportFormat.Csv, Columns, new object?[] { null, "n", 1234.5 });

            Assert.Equal(",n,1234.5\r\n", line);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Verifies that CSV header has the column names and a BOM constant is utf8.
    /// </summary>
    [Fact]
    public void CsvHeader_HasTheColumnNames_AndABomConstantIsUtf8()
    {
        Assert.Equal("who,note,rows\r\n", ExportFormatter.Header(ExportFormat.Csv, Columns));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, ExportFormatter.Bom);
    }

    /// <summary>
    /// Verifies that JSON lines is one object per line with numbers as numbers and no CSV prefixing.
    /// </summary>
    [Fact]
    public void JsonLines_IsOneObjectPerLine_WithNumbersAsNumbers_AndNoCsvPrefixing()
    {
        Assert.Equal("", ExportFormatter.Header(ExportFormat.JsonLines, Columns));

        var line = ExportFormatter.Row(ExportFormat.JsonLines, Columns, new object?[] { "=1+1", "ü \"q\"\n", 42 });

        Assert.EndsWith("\n", line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("=1+1", doc.RootElement.GetProperty("who").GetString());
        Assert.Equal("ü \"q\"\n", doc.RootElement.GetProperty("note").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("rows").GetInt32());
        Assert.Equal(new[] { "who", "note", "rows" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    /// <summary>
    /// Verifies that every data set has a documented schema with the time columns where times matter.
    /// </summary>
    [Fact]
    public void EveryDataSet_HasADocumentedSchema_WithTheTimeColumnsWhereTimesMatter()
    {
        foreach (var dataset in ExportDatasets.All)
        {
            var schema = ExportSchema.For(dataset);
            Assert.NotEmpty(schema);
            Assert.All(schema, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
        }
        Assert.Equal(new[] { "time_utc", "time_local", "time_zone" }, ExportSchema.For(ExportDatasets.Audit).Take(3).Select(c => c.Name));
        Assert.Equal(new[] { "time_utc", "time_local", "time_zone" }, ExportSchema.For(ExportDatasets.AccessLog).Take(3).Select(c => c.Name));
    }
}
