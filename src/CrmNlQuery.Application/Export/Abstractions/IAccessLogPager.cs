using CrmNlQuery.Application.Export.Models;
using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Application.Export.Abstractions;

/// <summary>
/// Pages the data access log for export.
/// </summary>
public interface IAccessLogPager
{
    /// <summary>
    /// Returns one page of access log rows in time then id order, applying the query's date, user, table and source filters and ignoring Take.
    /// </summary>
    Task<IReadOnlyList<DataAccessRecord>> PageAsync(DataAccessQuery query, AccessCursor? after, int size, CancellationToken ct = default);
}
