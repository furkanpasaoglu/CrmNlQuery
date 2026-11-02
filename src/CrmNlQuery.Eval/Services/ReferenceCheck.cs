using CrmNlQuery.Application.Quality.Comparison;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;

namespace CrmNlQuery.Eval.Services;

/// <summary>
/// Checks an eval case in reference-query mode by running the reference FetchXML on current data and comparing it to the candidate.
/// </summary>
public static class ReferenceCheck
{
    /// <summary>
    /// Holds the result of a reference check: pass flag, detail and reference row count.
    /// </summary>
    public sealed record Outcome(bool Pass, string Detail, int ReferenceRows, string Fingerprint);

    /// <summary>
    /// Reads the reference FetchXML from inline text or a file.
    /// </summary>
    public static string ReadFetchXml(EvalReference reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.FetchXml)) { return reference.FetchXml; }
        if (!string.IsNullOrWhiteSpace(reference.File)) { return File.ReadAllText(reference.File); }
        throw new InvalidOperationException("The reference has neither fetchXml nor file.");
    }

    /// <summary>
    /// Runs the reference and compares its result set with the answer produced by the candidate.
    /// </summary>
    public static async Task<Outcome> RunAsync(EvalReference reference, AnswerOutcome answer, Func<string, CancellationToken, Task<ResultTable>> runReference, CancellationToken ct)
    {
        string fetch;
        ResultTable table;
        try
        {
            (fetch, table) = await ExecuteAsync(reference, runReference, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Outcome(false, "the reference query could not run: " + ex.Message, 0, "");
        }

        var fingerprint = Fingerprint(table);
        var result = ReferenceComparer.Compare(answer.Table, table, reference.Compare, ReferenceQuery.HasOrder(fetch));
        var warning = table.Rows.Count == 0
            ? " [low-signal: the reference returns 0 rows]"
            : table.Rows.Count >= ReferenceQuery.RowCap ? " [low-signal: the reference hits the 500-row cap]" : "";
        return new Outcome(result.Pass, result.Detail + warning, table.Rows.Count, fingerprint);
    }

    /// <summary>
    /// Runs a single-query or multi-step reference and returns the final query and its result table.
    /// </summary>
    public static async Task<(string Fetch, ResultTable Table)> ExecuteAsync(EvalReference reference, Func<string, CancellationToken, Task<ResultTable>> run, CancellationToken ct)
    {
        if (reference.Steps is not { Count: > 0 } steps)
        {
            var single = ReferenceQuery.WithRowCap(ReadFetchXml(reference));
            return (single, await run(single, ct));
        }
        var ids = new List<string>();
        var haveIds = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var fetch = steps[i].FetchXml ?? (steps[i].File is { } path ? File.ReadAllText(path) : throw new InvalidOperationException("A reference step needs fetchXml or file."));
            if (haveIds)
            {
                if (ids.Count == 0)
                {
                    return (fetch, new ResultTable(Array.Empty<string>(), new List<IReadOnlyList<string>>()));
                }
                fetch = fetch.Replace("{{ids}}", string.Concat(ids.Select(id => "<value>" + id + "</value>")), StringComparison.Ordinal);
            }
            fetch = ReferenceQuery.WithRowCap(fetch);
            var table = await run(fetch, ct);
            if (i == steps.Count - 1) { return (fetch, table); }
            ids = Reduce(table, steps[i].Reduce);
            haveIds = true;
        }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// Keeps the first-column keys of the rows with the max or min last-column value, including ties.
    /// </summary>
    public static List<string> Reduce(ResultTable table, string mode)
    {
        var source = table.Raw ?? table.Rows;
        if (source.Count == 0) { return new List<string>(); }
        if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(mode))
        {
            return source.Select(r => r[0]).Distinct().ToList();
        }
        var values = source.Select(r => double.TryParse(r[^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToList();
        var target = string.Equals(mode, "max", StringComparison.OrdinalIgnoreCase) ? values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max() : values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min();
        var result = new List<string>();
        for (var i = 0; i < source.Count; i++)
        {
            if (values[i] == target) { result.Add(source[i][0]); }
        }
        return result.Distinct().ToList();
    }

    /// <summary>
    /// Builds a short fingerprint of a result table from row count and cell digest.
    /// </summary>
    public static string Fingerprint(ResultTable table) => ReferenceQuery.Fingerprint(table);
}
