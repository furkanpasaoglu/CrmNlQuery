namespace CrmNlQuery.Domain.Reports.Entities;

/// <summary>
/// Represents a report catalog entry defined by an admin, either an alias for a CRM report or an external address.
/// </summary>
public sealed record ReportCatalogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public string? CrmReportId { get; init; }
    public string? Url { get; init; }
    public string? RecordEntity { get; init; }
    public string? RecordParam { get; init; }
    public bool Enabled { get; init; } = true;
    public string UpdatedBy { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
}
