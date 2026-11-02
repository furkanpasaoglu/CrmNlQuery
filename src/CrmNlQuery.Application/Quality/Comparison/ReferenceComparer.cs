using System.Globalization;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Quality.Comparison;

/// <summary>
/// Rule-based comparison of a candidate result table with a reference query's table (no network, no model), with per-case order, column and tolerance rules.
/// </summary>
public static class ReferenceComparer
{
    /// <summary>
    /// Outcome of a reference comparison: pass flag and a detail text.
    /// </summary>
    public sealed record Result(bool Pass, string Detail);

    /// <summary>
    /// Compares candidate and reference tables under the case rules; with showValues false the detail contains no cell values, so stored quality reports stay safe to share.
    /// </summary>
    public static Result Compare(ResultTable candidate, ResultTable reference, CompareRule? options, bool referenceHasOrder, bool showValues = true)
    {
        var rule = options ?? new CompareRule();
        if (reference.Rows.Count == 0)
        {
            return candidate.Rows.Count == 0
                ? new Result(true, "ok: both are empty (matches the reference)")
                : new Result(false, $"{candidate.Rows.Count} rows, the reference is empty");
        }
        var mapping = MapColumns(candidate, reference, rule);
        if (mapping.Error is not null)
        {
            return new Result(false, mapping.Error);
        }

        var tolerance = rule.Tolerance ?? 0.01;
        var candidateRows = Project(candidate, mapping.Candidate, tolerance);
        var referenceRows = Project(reference, mapping.Reference, tolerance);

        var ordered = rule.Order switch
        {
            "ordered" => true,
            "unordered" => false,
            _ => referenceHasOrder,
        };
        var set = string.Equals(rule.Duplicates, "set", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(rule.Ties, "boundary", StringComparison.OrdinalIgnoreCase))
        {
            return Boundary(candidateRows, referenceRows, set, showValues);
        }

        if (ordered)
        {
            return Ordered(candidateRows, referenceRows, showValues);
        }
        return Unordered(candidateRows, referenceRows, set, showValues);
    }

    private static (int[] Candidate, int[] Reference, string? Error) MapColumns(ResultTable candidate, ResultTable reference, CompareRule rule)
    {
        if (string.Equals(rule.Columns, "last", StringComparison.OrdinalIgnoreCase))
        {
            return (new[] { candidate.Columns.Count - 1 }, new[] { reference.Columns.Count - 1 }, null);
        }
        var exact = string.Equals(rule.Columns, "exact", StringComparison.OrdinalIgnoreCase);
        var candidateIndexes = new List<int>();
        var referenceIndexes = new List<int>();
        var missing = new List<string>();
        for (var r = 0; r < reference.Columns.Count; r++)
        {
            var at = IndexOf(candidate.Columns, reference.Columns[r]);
            if (at < 0)
            {
                missing.Add(reference.Columns[r]);
                continue;
            }
            referenceIndexes.Add(r);
            candidateIndexes.Add(at);
        }
        if (missing.Count > 0)
        {
            if (candidate.Columns.Count == reference.Columns.Count)
            {
                return (Enumerable.Range(0, candidate.Columns.Count).ToArray(), Enumerable.Range(0, reference.Columns.Count).ToArray(), null);
            }
            return (Array.Empty<int>(), Array.Empty<int>(), $"expected column(s) [{string.Join(", ", missing)}] not found; the answer has [{string.Join(", ", candidate.Columns)}]");
        }
        if (exact && candidate.Columns.Count != reference.Columns.Count)
        {
            return (Array.Empty<int>(), Array.Empty<int>(), $"columns were [{string.Join(", ", candidate.Columns)}], expected exactly [{string.Join(", ", reference.Columns)}]");
        }
        return (candidateIndexes.ToArray(), referenceIndexes.ToArray(), null);
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return -1;
    }

    private static List<string> Project(ResultTable table, int[] columns, double tolerance)
    {
        var source = table.Raw ?? table.Rows;
        var result = new List<string>();
        foreach (var row in source)
        {
            result.Add(string.Join("\u001f", columns.Select(c => c < row.Count ? Normalize(row[c], tolerance) : "")));
        }
        return result;
    }

    private static string Normalize(string cell, double tolerance)
    {
        var text = (cell ?? "").Trim();
        if (text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            if (tolerance > 0)
            {
                number = Math.Round(number / tolerance, MidpointRounding.AwayFromZero) * tolerance;
            }
            return "#" + number.ToString("0.########", CultureInfo.InvariantCulture);
        }
        return text.ToLowerInvariant();
    }

    private static Result Ordered(List<string> candidate, List<string> reference, bool showValues)
    {
        if (candidate.Count != reference.Count)
        {
            return new Result(false, $"{candidate.Count} rows, the reference has {reference.Count}" + Diff(candidate, reference, showValues));
        }
        for (var i = 0; i < candidate.Count; i++)
        {
            if (candidate[i] != reference[i])
            {
                return new Result(false, showValues
                    ? $"row {i + 1} differs from the reference (order matters): got '{Show(candidate[i])}', expected '{Show(reference[i])}'"
                    : $"row {i + 1} differs from the reference (order matters)");
            }
        }
        return new Result(true, $"ok: matches the reference ({reference.Count} rows, ordered)");
    }

    private static Result Unordered(List<string> candidate, List<string> reference, bool set, bool showValues)
    {
        var c = set ? candidate.Distinct().ToList() : candidate;
        var r = set ? reference.Distinct().ToList() : reference;
        var missing = Subtract(r, c);
        var extra = Subtract(c, r);
        if (missing.Count == 0 && extra.Count == 0)
        {
            return new Result(true, $"ok: matches the reference ({r.Count} rows)");
        }
        return new Result(false, $"{c.Count} rows, the reference has {r.Count}" + Describe(missing, extra, showValues));
    }

    private static Result Boundary(List<string> candidate, List<string> reference, bool set, bool showValues)
    {
        var extra = Subtract(candidate, reference);
        if (extra.Count > 0)
        {
            return new Result(false, showValues
                ? $"{extra.Count} row(s) are not in the reference, for example '{Show(extra[0])}'"
                : $"{extra.Count} row(s) are not in the reference");
        }
        return new Result(true, $"ok: {candidate.Count} rows are all in the reference ({reference.Count} rows; ties at the boundary accepted)");
    }

    private static List<string> Subtract(List<string> a, List<string> b)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in b)
        {
            counts[item] = counts.TryGetValue(item, out var n) ? n + 1 : 1;
        }
        var result = new List<string>();
        foreach (var item in a)
        {
            if (counts.TryGetValue(item, out var n) && n > 0) { counts[item] = n - 1; }
            else { result.Add(item); }
        }
        return result;
    }

    private static string Diff(List<string> candidate, List<string> reference, bool showValues)
        => Describe(Subtract(reference, candidate), Subtract(candidate, reference), showValues);

    private static string Describe(List<string> missing, List<string> extra, bool showValues)
    {
        var parts = new List<string>();
        if (missing.Count > 0) { parts.Add(showValues ? $"missing {missing.Count}: {string.Join("; ", missing.Take(3).Select(Show))}" : $"missing {missing.Count}"); }
        if (extra.Count > 0) { parts.Add(showValues ? $"extra {extra.Count}: {string.Join("; ", extra.Take(3).Select(Show))}" : $"extra {extra.Count}"); }
        return parts.Count == 0 ? "" : " (" + string.Join(" | ", parts) + ")";
    }

    private static string Show(string row)
    {
        var text = row.Replace("\u001f", " | ");
        return text.Length > 80 ? text.Substring(0, 80) + "…" : text;
    }
}
