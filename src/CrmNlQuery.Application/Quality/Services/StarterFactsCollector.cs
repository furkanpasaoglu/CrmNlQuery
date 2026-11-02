using System.Globalization;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Quality.Services;

/// <summary>
/// Collects the facts the starter set generator needs from current data using read-only aggregate queries, never reading or storing rows.
/// </summary>
public static class StarterFactsCollector
{
    /// <summary>
    /// Delegate that runs a FetchXML query and returns its result table.
    /// </summary>
    public delegate Task<ResultTable> RunQuery(string fetchXml, CancellationToken ct);

    /// <summary>
    /// Counts the records of a table using the given query runner, returning null if counting fails.
    /// </summary>
    public static Task<int?> CountRecordsAsync(EntityInfo info, RunQuery run, Action<string>? log, CancellationToken ct)
        => CountAsync(run, $"<fetch aggregate='true'><entity name='{info.LogicalName}'><attribute name='{info.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>", log, ct);

    /// <summary>
    /// Collects record counts, option distributions and other facts for the chosen entities, skipping failed queries.
    /// </summary>
    public static async Task<StarterFacts> CollectAsync(IReadOnlyList<EntityInfo> chosen, IReadOnlyDictionary<string, int> counts, RunQuery run, Action<string>? log, CancellationToken ct)
    {
        var options = new Dictionary<string, IReadOnlyDictionary<int, int>>();
        var filled = new Dictionary<string, int>();
        var groups = new Dictionary<string, int>();
        foreach (var info in chosen)
        {
            var probes = StarterSetGenerator.Probes(info);
            foreach (var attribute in probes.Categorical)
            {
                var rows = await RowsAsync(run, $"<fetch aggregate='true'><entity name='{info.LogicalName}'><attribute name='{attribute.LogicalName}' groupby='true' alias='g'/><attribute name='{info.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>", log, ct);
                if (rows is null) { continue; }
                var byValue = new Dictionary<int, int>();
                foreach (var row in rows)
                {
                    if (int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && int.TryParse(row[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) { byValue[value] = count; }
                }
                options[StarterSetGenerator.Key(info, attribute)] = byValue;
            }
            foreach (var attribute in probes.Numeric)
            {
                var count = await CountAsync(run, $"<fetch aggregate='true'><entity name='{info.LogicalName}'><attribute name='{info.PrimaryIdAttribute}' aggregate='count' alias='n'/><filter><condition attribute='{attribute.LogicalName}' operator='not-null'/></filter></entity></fetch>", log, ct);
                if (count is int c) { filled[StarterSetGenerator.Key(info, attribute)] = c; }
            }
            foreach (var (attribute, _) in probes.Lookups)
            {
                var rows = await RowsAsync(run, $"<fetch aggregate='true'><entity name='{info.LogicalName}'><attribute name='{attribute.LogicalName}' groupby='true' alias='g'/><attribute name='{info.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>", log, ct);
                if (rows is not null) { groups[StarterSetGenerator.Key(info, attribute)] = rows.Count; }
            }
        }
        return new StarterFacts(counts, options, filled, groups);
    }

    private static async Task<int?> CountAsync(RunQuery run, string fetchXml, Action<string>? log, CancellationToken ct)
    {
        var rows = await RowsAsync(run, fetchXml, log, ct);
        return rows is { Count: > 0 } && int.TryParse(rows[0][^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>?> RowsAsync(RunQuery run, string fetchXml, Action<string>? log, CancellationToken ct)
    {
        try
        {
            var table = await run(fetchXml, ct);
            return table.Raw ?? table.Rows;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke("query skipped: " + ex.Message.Split('\n')[0]);
            return null;
        }
    }
}
