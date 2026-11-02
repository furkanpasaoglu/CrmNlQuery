using CrmNlQuery.Application.DataPolicy.Services;

namespace CrmNlQuery.Application.DataPolicy.Abstractions;

/// <summary>
/// Port that provides the current data policy snapshot.
/// </summary>
public interface IDataPolicy
{
    /// <summary>
    /// Returns the current data policy snapshot that decides which tables and fields are open to questions.
    /// </summary>
    Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default);
}
