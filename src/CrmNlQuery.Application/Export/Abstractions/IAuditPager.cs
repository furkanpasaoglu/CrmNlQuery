using CrmNlQuery.Application.Export.Models;

namespace CrmNlQuery.Application.Export.Abstractions;

/// <summary>
/// Pages the audit log with a keyset cursor so large exports do not load into memory.
/// </summary>
public interface IAuditPager
{
    /// <summary>
    /// Returns one page of audit entries in the time range, optionally filtered by exact actor and action prefix.
    /// </summary>
    Task<IReadOnlyList<AuditPageItem>> PageAuditAsync(DateTimeOffset from, DateTimeOffset to, string? actor, string? actionPrefix, AuditCursor? after, int size, CancellationToken ct = default);
}
