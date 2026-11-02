namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Carries the facts gathered from the CRM (record counts, option distributions and similar) used to generate the starter acceptance set.
/// </summary>
public sealed record StarterFacts(
    IReadOnlyDictionary<string, int> RecordCounts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> OptionCounts,
    IReadOnlyDictionary<string, int> FilledCounts,
    IReadOnlyDictionary<string, int> GroupCounts);
