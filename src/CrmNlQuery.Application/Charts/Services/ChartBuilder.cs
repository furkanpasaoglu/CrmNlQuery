using System.Globalization;
using System.Xml.Linq;
using CrmNlQuery.Application.Charts.Models;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Charts.Services;

/// <summary>
/// Builds chart data from its inputs.
/// </summary>
public static class ChartBuilder
{
    public const int MaxCategories = 10;
    private const int MaxPoints = 400;
    private const int LongLabel = 14;

    private sealed record Column(int Index, string Alias, string? DateGrouping);

    /// <summary>
    /// Builds chart data from an aggregate result's FetchXML and table (marking truncation), or returns null when no suitable chart exists.
    /// </summary>
    public static ChartData? Build(string fetchXml, ResultTable table, bool truncated)
    {
        if (table.Raw is null || table.Rows.Count < 2)
        {
            return null;
        }
        XElement? root;
        try
        {
            root = XDocument.Parse(fetchXml).Root;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        if (root is null || !string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var groups = new List<Column>();
        var measures = new List<(Column Column, string Fn)>();
        var index = 0;
        foreach (var attribute in root.Descendants("attribute"))
        {
            var alias = (string?)attribute.Attribute("alias") ?? (string?)attribute.Attribute("name") ?? "";
            if (alias.EndsWith("_rowid", StringComparison.Ordinal))
            {
                continue;
            }
            var column = new Column(index++, alias, (string?)attribute.Attribute("dategrouping"));
            if (string.Equals((string?)attribute.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase))
            {
                groups.Add(column);
            }
            else if (attribute.Attribute("aggregate") is { } fn)
            {
                measures.Add((column, fn.Value.ToLowerInvariant()));
            }
        }
        if (measures.Count != 1 || groups.Count is < 1 or > 2 || table.Columns.Count != index)
        {
            return null;
        }
        var measure = measures[0];
        var hasOrder = root.Descendants("order").Any();

        ChartData? chart;
        if (groups.Count == 1)
        {
            chart = groups[0].DateGrouping is null
                ? Categories(table, groups[0], measure, hasOrder)
                : SingleDate(table, groups[0], measure);
        }
        else if (groups[0].DateGrouping is not null && groups[1].DateGrouping is not null)
        {
            chart = TwoDates(table, groups[0], groups[1], measure);
        }
        else
        {
            chart = null;
        }
        return chart is null || chart.XLabels.Count < 2 || chart.XLabels.Count > MaxPoints ? null : chart with { Partial = truncated };
    }

    /// <summary>
    /// Handles why not for chart builder.
    /// </summary>
    public static string? WhyNot(string fetchXml, ResultTable table)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(fetchXml).Root;
        }
        catch (System.Xml.XmlException)
        {
            return "unsupported";
        }
        if (root is null || !string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return "list";
        }
        var groups = 0;
        var measures = 0;
        foreach (var attribute in root.Descendants("attribute"))
        {
            var alias = (string?)attribute.Attribute("alias") ?? (string?)attribute.Attribute("name") ?? "";
            if (alias.EndsWith("_rowid", StringComparison.Ordinal))
            {
                continue;
            }
            if (string.Equals((string?)attribute.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase))
            {
                groups++;
            }
            else if (attribute.Attribute("aggregate") is not null)
            {
                measures++;
            }
        }
        if (groups == 0)
        {
            return "list";
        }
        if (measures != 1 || groups > 2)
        {
            return "unsupported";
        }
        return table.Rows.Count < 2 ? "few" : "unsupported";
    }

    private static double? Number(ResultTable table, int row, int column)
    {
        var raw = table.Raw![row];
        return column < raw.Count && double.TryParse(raw[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int? Whole(ResultTable table, int row, int column)
    {
        var number = Number(table, row, column);
        return number is double d && d == Math.Floor(d) && d is > -100000 and < 100000 ? (int)d : null;
    }

    private static ChartData? Categories(ResultTable table, Column group, (Column Column, string Fn) measure, bool hasOrder)
    {
        var points = new List<(string Label, double Value)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var label = group.Index < table.Rows[r].Count ? table.Rows[r][group.Index] : "";
            var value = Number(table, r, measure.Column.Index);
            if (value is double v)
            {
                points.Add((string.IsNullOrWhiteSpace(label) ? "(empty)" : label, v));
            }
        }
        if (points.Count == 0)
        {
            return null;
        }
        if (!hasOrder || points.Count > MaxCategories)
        {
            points = points.OrderByDescending(p => p.Value).ToList();
        }
        var omitted = 0;
        var hasOther = false;
        if (points.Count > MaxCategories)
        {
            var rest = points.Skip(MaxCategories).ToList();
            points = points.Take(MaxCategories).ToList();
            if (measure.Fn is "sum" or "count" or "countcolumn")
            {
                points.Add(("Other", rest.Sum(p => p.Value)));
                hasOther = true;
            }
            else
            {
                omitted = rest.Count;
            }
        }
        var kind = points.Any(p => p.Label.Length > LongLabel) ? "hbar" : "bar";
        return new ChartData(kind, "category", measure.Column.Alias, measure.Fn, points.Select(p => p.Label).ToList(),
            new[] { new ChartSeries(measure.Column.Alias, points.Select(p => (double?)p.Value).ToList()) })
        { HasOther = hasOther, OmittedCategories = omitted };
    }

    private static ChartData? SingleDate(ResultTable table, Column group, (Column Column, string Fn) measure)
    {
        var unit = group.DateGrouping!.ToLowerInvariant();
        var byKey = new SortedDictionary<int, double>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            if (Whole(table, r, group.Index) is int key && Number(table, r, measure.Column.Index) is double value)
            {
                byKey[key] = byKey.TryGetValue(key, out var old) ? old + value : value;
            }
        }
        if (byKey.Count == 0)
        {
            return null;
        }
        var keys = unit is "year" or "quarter" or "month"
            ? Enumerable.Range(byKey.Keys.First(), byKey.Keys.Last() - byKey.Keys.First() + 1).ToList()
            : byKey.Keys.ToList();
        return new ChartData("line", unit, measure.Column.Alias, measure.Fn, keys.Select(k => k.ToString(CultureInfo.InvariantCulture)).ToList(),
            new[] { new ChartSeries(measure.Column.Alias, keys.Select(k => (double?)(byKey.TryGetValue(k, out var v) ? v : 0)).ToList()) });
    }

    private static ChartData? TwoDates(ResultTable table, Column first, Column second, (Column Column, string Fn) measure)
    {
        var yearColumn = string.Equals(first.DateGrouping, "year", StringComparison.OrdinalIgnoreCase) ? first
            : string.Equals(second.DateGrouping, "year", StringComparison.OrdinalIgnoreCase) ? second : null;
        if (yearColumn is null)
        {
            return null;
        }
        var other = yearColumn == first ? second : first;
        var unit = other.DateGrouping!.ToLowerInvariant();
        if (unit is not ("month" or "quarter"))
        {
            return null;
        }
        var perMonthsInYear = unit == "month" ? 12 : 4;

        var cells = new Dictionary<(int Year, int Part), double>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            if (Whole(table, r, yearColumn.Index) is int year && Whole(table, r, other.Index) is int part
                && part >= 1 && part <= perMonthsInYear && Number(table, r, measure.Column.Index) is double value)
            {
                cells[(year, part)] = cells.TryGetValue((year, part), out var old) ? old + value : value;
            }
        }
        if (cells.Count == 0)
        {
            return null;
        }
        var years = cells.Keys.Select(k => k.Year).Distinct().OrderBy(y => y).ToList();
        var repeated = cells.Keys.GroupBy(k => k.Part).Any(g => g.Select(k => k.Year).Distinct().Count() > 1);

        if (years.Count > 1 && repeated)
        {
            var parts = Enumerable.Range(cells.Keys.Min(k => k.Part), cells.Keys.Max(k => k.Part) - cells.Keys.Min(k => k.Part) + 1).ToList();
            return new ChartData("line", unit, measure.Column.Alias, measure.Fn, parts.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList(),
                years.Select(y => new ChartSeries(y.ToString(CultureInfo.InvariantCulture), parts.Select(p => (double?)(cells.TryGetValue((y, p), out var v) ? v : 0)).ToList())).ToList());
        }

        var start = cells.Keys.OrderBy(k => k.Year).ThenBy(k => k.Part).First();
        var end = cells.Keys.OrderBy(k => k.Year).ThenBy(k => k.Part).Last();
        var labels = new List<string>();
        var values = new List<double?>();
        var (y2, p2) = start;
        while (y2 < end.Year || (y2 == end.Year && p2 <= end.Part))
        {
            labels.Add(y2.ToString("0000", CultureInfo.InvariantCulture) + "-" + (unit == "month" ? p2.ToString("00", CultureInfo.InvariantCulture) : p2.ToString(CultureInfo.InvariantCulture)));
            values.Add(cells.TryGetValue((y2, p2), out var v) ? v : 0);
            if (++p2 > perMonthsInYear)
            {
                p2 = 1;
                y2++;
            }
        }
        return new ChartData("line", unit == "month" ? "yearmonth" : "yearquarter", measure.Column.Alias, measure.Fn, labels,
            new[] { new ChartSeries(measure.Column.Alias, values) });
    }
}
