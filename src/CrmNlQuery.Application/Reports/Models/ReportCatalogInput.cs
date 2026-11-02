namespace CrmNlQuery.Application.Reports.Models;

/// <summary>
/// Fields an admin enters for a report catalog entry, validated before saving.
/// </summary>
public sealed record ReportCatalogInput(string? Id, string? Name, string? Description, IReadOnlyList<string>? Keywords, string? CrmReportId, string? Url,
    string? RecordEntity, string? RecordParam, bool Enabled);
