using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Application.Reports.Abstractions;

/// <summary>
/// Stores the report catalog in memory and PostgreSQL; entries are removed only by explicit deletion.
/// </summary>
public interface IReportCatalogStore
{
    bool Persistent { get; }

    /// <summary>
    /// Returns every entry in the report catalog from the underlying store.
    /// </summary>
    Task<IReadOnlyList<ReportCatalogEntry>> ListAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets a report catalog entry by id, or null.
    /// </summary>
    Task<ReportCatalogEntry?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Adds a report catalog entry or updates it if it exists.
    /// </summary>
    Task UpsertAsync(ReportCatalogEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Deletes a report catalog entry and returns whether it existed.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
