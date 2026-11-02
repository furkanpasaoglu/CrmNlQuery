using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Abstractions;

/// <summary>
/// Store for the acceptance set; cases are kept indefinitely and only removed when explicitly deleted or retired.
/// </summary>
public interface IQualityStore
{
    bool Persistent { get; }

    /// <summary>
    /// Adds a case to the acceptance set.
    /// </summary>
    Task AddAsync(QualityCase item, CancellationToken ct = default);
    /// <summary>
    /// Lists acceptance-set cases matching a filter.
    /// </summary>
    Task<IReadOnlyList<QualityCase>> ListAsync(QualityCaseFilter filter, CancellationToken ct = default);
    /// <summary>
    /// Gets one acceptance-set case by id.
    /// </summary>
    Task<QualityCase?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Returns how many acceptance-set cases are stored, used to enforce the case limit.
    /// </summary>
    Task<int> CountAsync(CancellationToken ct = default);
    /// <summary>
    /// Returns true if the same question with the same reference query already exists, so duplicates are not added.
    /// </summary>
    Task<bool> ExistsAsync(string question, string referenceFetch, CancellationToken ct = default);
    /// <summary>
    /// Sets the status of the given cases and returns how many were affected.
    /// </summary>
    Task<int> SetStatusAsync(IReadOnlyCollection<string> ids, string status, CancellationToken ct = default);
    /// <summary>
    /// Marks the given cases as verified or not verified.
    /// </summary>
    Task<int> SetVerifiedAsync(IReadOnlyCollection<string> ids, bool verified, CancellationToken ct = default);
    /// <summary>
    /// Deletes the acceptance-set cases with the given ids and returns how many were removed.
    /// </summary>
    Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
}
