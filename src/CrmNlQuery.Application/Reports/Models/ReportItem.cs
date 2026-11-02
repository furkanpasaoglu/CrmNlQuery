namespace CrmNlQuery.Application.Reports.Models;

/// <summary>
/// Represents a CRM report with id, name, short description and the URL that opens it in the report viewer.
/// </summary>
public sealed record ReportItem(string Id, string Name, string? Description, string Url)
{
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public string? RecordEntity { get; init; }
    public string? RecordParam { get; init; }
    public string? ForRecord { get; init; }
}
