using CrmNlQuery.Application.Suggestions.Models;

namespace CrmNlQuery.Application.Suggestions.Abstractions;

/// <summary>
/// Stores daily counters of suggestion kinds shown and clicked.
/// </summary>
public interface ISuggestionStats
{
    /// <summary>
    /// Adds shown counts for the given suggestion kinds.
    /// </summary>
    Task AddShownAsync(IReadOnlyList<string> kinds, CancellationToken ct = default);

    /// <summary>
    /// Adds a click count for a suggestion kind.
    /// </summary>
    Task AddClickAsync(string kind, CancellationToken ct = default);

    /// <summary>
    /// Returns per-kind totals for the last given number of days including today.
    /// </summary>
    Task<IReadOnlyList<SuggestionStat>> SummaryAsync(int days, CancellationToken ct = default);

    /// <summary>
    /// Deletes suggestion counters older than the cutoff and returns how many were removed.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
