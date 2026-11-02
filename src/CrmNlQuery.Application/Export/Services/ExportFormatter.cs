using System.Globalization;
using System.Text;
using CrmNlQuery.Application.Export.Models;

namespace CrmNlQuery.Application.Export.Services;

/// <summary>
/// Formats export rows as CSV or JSON Lines, neutralizing formula injection in text columns.
/// </summary>
public static class ExportFormatter
{
    public static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>
    /// Returns the header line for the export format.
    /// </summary>
    public static string Header(ExportFormat format, IReadOnlyList<ExportColumn> columns)
    {
        return format == ExportFormat.Csv ? string.Join(",", columns.Select(c => Quote(c.Name))) + "\r\n" : "";
    }

    /// <summary>
    /// Formats one export row as either a CSV line or a JSON Lines object, depending on the chosen format.
    /// </summary>
    public static string Row(ExportFormat format, IReadOnlyList<ExportColumn> columns, IReadOnlyList<object?> values)
    {
        return format == ExportFormat.Csv ? CsvRow(columns, values) : JsonRow(columns, values);
    }

    private static string CsvRow(IReadOnlyList<ExportColumn> columns, IReadOnlyList<object?> values)
    {
        var cells = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var value = i < values.Count ? values[i] : null;
            cells[i] = value switch
            {
                null => "",
                string text => Quote(columns[i].IsText ? Neutralize(text) : text),
                IFormattable number => Quote(number.ToString(null, CultureInfo.InvariantCulture)),
                _ => Quote(value.ToString() ?? ""),
            };
        }
        return string.Join(",", cells) + "\r\n";
    }

    /// <summary>
    /// Neutralizes text starting with characters that could run as a formula.
    /// </summary>
    public static string Neutralize(string text)
    {
        return text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
    }

    private static string Quote(string text)
    {
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static string JsonRow(IReadOnlyList<ExportColumn> columns, IReadOnlyList<object?> values)
    {
        var sb = new StringBuilder("{");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(System.Text.Json.JsonSerializer.Serialize(columns[i].Name)).Append(':');
            var value = i < values.Count ? values[i] : null;
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string text:
                    sb.Append(System.Text.Json.JsonSerializer.Serialize(text));
                    break;
                case IFormattable number:
                    sb.Append(number.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(System.Text.Json.JsonSerializer.Serialize(value.ToString()));
                    break;
            }
        }
        return sb.Append("}\n").ToString();
    }
}
