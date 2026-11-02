using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Application.Reports.Models;

/// <summary>
/// Result of saving a report catalog entry, holding either an error code or the saved entry.
/// </summary>
public sealed record ReportCatalogResult(string? Error, ReportCatalogEntry? Entry = null)
{
    public bool Ok => Error is null;
}
