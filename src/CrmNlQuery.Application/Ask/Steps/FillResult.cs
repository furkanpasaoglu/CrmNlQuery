namespace CrmNlQuery.Application.Ask.Steps;

/// <summary>
/// Result of filling placeholders: status, resulting FetchXML, step number, id count and any errors.
/// </summary>
public sealed record FillResult(FillStatus Status, string? FetchXml, int Step, int Count, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Creates a filled result for fill result.
    /// </summary>
    public static FillResult Filled(string fetchXml) => new FillResult(FillStatus.Filled, fetchXml, 0, 0, new List<string>());
}
