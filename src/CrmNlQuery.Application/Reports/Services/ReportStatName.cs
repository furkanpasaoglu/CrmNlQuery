namespace CrmNlQuery.Application.Reports.Services;

/// <summary>
/// Helpers for normalizing report names used as counter keys.
/// </summary>
public static class ReportStatName
{
    public const int MaxLength = 120;

    /// <summary>
    /// Normalizes a report name for use as a counter key by collapsing whitespace and limiting length; returns null if empty so it is not counted.
    /// </summary>
    public static string? Clean(string? name)
    {
        var one = string.Join(" ", (name ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        if (one.Length == 0) { return null; }
        return one.Length <= MaxLength ? one : one.Substring(0, MaxLength);
    }
}
