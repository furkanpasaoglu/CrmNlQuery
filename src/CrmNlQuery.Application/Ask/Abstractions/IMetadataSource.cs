using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Port that supplies CRM schema metadata, entity summaries and detailed entity info, to the planning and validation code.
/// </summary>
public interface IMetadataSource
{
    /// <summary>
    /// Returns summaries of the entities available in the metadata source.
    /// </summary>
    Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns one entity's metadata, or null for an unknown entity because the model may invent names.
    /// </summary>
    Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the metadata from its source.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
