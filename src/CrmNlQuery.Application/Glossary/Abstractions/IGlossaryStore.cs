using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Application.Glossary.Models;

namespace CrmNlQuery.Application.Glossary.Abstractions;

/// <summary>
/// Stores business glossary entries, in memory and PostgreSQL, enforcing term uniqueness across all entries and languages.
/// </summary>
public interface IGlossaryStore
{
    bool Persistent { get; }

    /// <summary>
    /// Returns every stored glossary entry, ordered consistently by the store implementation.
    /// </summary>
    Task<IReadOnlyList<GlossaryEntry>> ListAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets a glossary entry by id, or null.
    /// </summary>
    Task<GlossaryEntry?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Adds a glossary entry, throwing a duplicate-term exception when a term already exists.
    /// </summary>
    Task AddAsync(GlossaryEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Updates a glossary entry and returns whether it existed.
    /// </summary>
    Task<bool> UpdateAsync(GlossaryEntry entry, CancellationToken ct = default);
    /// <summary>
    /// Deletes a glossary entry and returns whether it existed.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Records per entry whether the glossary was applied or could not be applied.
    /// </summary>
    Task RecordAsync(string entryId, bool applied, DateTimeOffset at, CancellationToken ct = default);
    /// <summary>
    /// Returns the per-entry applied and not-applied counters.
    /// </summary>
    Task<IReadOnlyList<GlossaryStats>> StatsAsync(CancellationToken ct = default);
}
