namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Provides the default starting list of open tables for tables with no admin decision.
/// </summary>
public static class DataPolicyDefaults
{
    public static IReadOnlyCollection<string> Tables { get; } = new[]
    {
        "account", "contact", "opportunity", "product", "contract", "contractdetail", "incident", "salesorder",
    };

    /// <summary>
    /// Parses the configured table list, using the built-in list when empty.
    /// </summary>
    public static IReadOnlyCollection<string> Parse(string? configured)
    {
        var parsed = (configured ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .ToList();
        return parsed.Count == 0 ? Tables : parsed;
    }
}
