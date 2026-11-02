using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Test metadata source that serves canned entity metadata.
/// </summary>
public sealed class FakeMetadataSource : IMetadataSource
{
    private readonly Dictionary<string, EntityInfo> _entities;

    public FakeMetadataSource(params EntityInfo[] entities)
    {
        _entities = entities.ToDictionary(e => e.LogicalName);
    }

    /// <summary>
    /// Returns summaries of all canned entities held by the fake metadata source, built from their names and labels.
    /// </summary>
    public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<EntitySummary> list = _entities.Values
            .Select(e => new EntitySummary(e.LogicalName, e.EntitySetName, e.Label)).ToList();
        return Task.FromResult(list);
    }

    /// <summary>
    /// Returns one canned entity or null if unknown.
    /// </summary>
    public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
    {
        _entities.TryGetValue(logicalName, out var entity);
        return Task.FromResult(entity);
    }

    /// <summary>
    /// Does nothing in the fake, standing in for a metadata refresh.
    /// </summary>
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
