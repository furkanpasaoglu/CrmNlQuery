namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Carries a report catalog entry submitted from the panel.
/// </summary>
public sealed record ReportCatalogDto(string? Name, string? Description, List<string>? Keywords, string? CrmReportId, string? Url, string? RecordEntity, string? RecordParam, bool? Enabled);
