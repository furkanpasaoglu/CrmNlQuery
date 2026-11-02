using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Port that searches CRM records by name.
/// </summary>
public interface IRecordSearcher
{
    /// <summary>
    /// Searches records of an entity by name text as the given caller.
    /// </summary>
    Task<IReadOnlyList<RecordMatch>> SearchByNameAsync(EntityInfo entity, string text, Caller caller, CancellationToken ct = default);
}
