namespace CrmNlQuery.Application.Suggestions.Services;

/// <summary>
/// Holds the known suggestion kinds such as narrow, group, related, time and sort that counters are recorded under.
/// </summary>
public static class SuggestionKinds
{
    public static readonly IReadOnlyList<string> All = new[] { "narrow", "group", "related", "time", "sort" };

    /// <summary>
    /// Checks whether a string is a known suggestion kind.
    /// </summary>
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}
