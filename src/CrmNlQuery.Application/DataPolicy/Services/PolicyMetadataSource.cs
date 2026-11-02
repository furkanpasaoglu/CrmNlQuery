using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Models;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Wraps the metadata source as the single enforcement point of the data allowlist, hiding closed tables and fields from every consumer.
/// </summary>
public sealed class PolicyMetadataSource : IMetadataSource
{
    private static readonly HashSet<string> LookupTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Lookup", "Customer", "Owner" };

    private static readonly HashSet<string> NameOnlyTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "systemuser", "team", "owner" };

    private readonly IMetadataSource _inner;
    private readonly IDataPolicy _policy;
    private readonly object _gate = new object();
    private string? _intersectVersion;
    private HashSet<string>? _intersects;

    public IMetadataSource Unfiltered => _inner;

    public PolicyMetadataSource(IMetadataSource inner, IDataPolicy policy)
    {
        _inner = inner;
        _policy = policy;
    }

    /// <summary>
    /// Lists only the tables the data policy leaves open.
    /// </summary>
    public async Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
    {
        var all = await _inner.ListEntitiesAsync(ct);
        var snapshot = await _policy.GetAsync(ct);
        if (!snapshot.Enabled)
        {
            return all;
        }
        return all.Where(e => snapshot.IsTableOpen(e.LogicalName)).ToList();
    }

    /// <summary>
    /// Returns the entity with closed fields and relations removed, or null for a closed table.
    /// </summary>
    public async Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
    {
        var entity = await _inner.GetEntityAsync(logicalName, ct);
        var snapshot = await _policy.GetAsync(ct);
        if (entity is null || !snapshot.Enabled)
        {
            return entity;
        }
        if (!await TableUsableAsync(entity.LogicalName, snapshot, ct))
        {
            return null;
        }
        return await FilterAsync(entity, snapshot, ct);
    }

    /// <summary>
    /// Refreshes the underlying metadata source and clears the cached many-to-many intersect table set so it is recomputed.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _inner.RefreshAsync(ct);
        lock (_gate)
        {
            _intersectVersion = null;
            _intersects = null;
        }
    }

    private async Task<EntityInfo> FilterAsync(EntityInfo entity, DataPolicySnapshot snapshot, CancellationToken ct)
    {
        var cache = new Dictionary<string, EntityInfo?>(StringComparer.OrdinalIgnoreCase) { [entity.LogicalName] = entity };

        async Task<EntityInfo?> InfoAsync(string name)
        {
            if (!cache.TryGetValue(name, out var info))
            {
                info = await _inner.GetEntityAsync(name, ct);
                cache[name] = info;
            }
            return info;
        }

        async Task<bool> AttributeOpenAsync(string entityName, string attribute)
        {
            if (!await TableUsableAsync(entityName, snapshot, ct))
            {
                return false;
            }
            var info = await InfoAsync(entityName);
            return info is not null && IsAttributeOpen(info, attribute, snapshot);
        }

        var attributes = entity.Attributes.Where(a => IsAttributeOpen(entity, a.LogicalName, snapshot)).ToList();

        var relationships = new List<RelationshipInfo>();
        foreach (var r in entity.Relationships)
        {
            if (await AttributeOpenAsync(r.ReferencingEntity, r.ReferencingAttribute) && await AttributeOpenAsync(r.ReferencedEntity, r.ReferencedAttribute))
            {
                relationships.Add(r);
            }
        }

        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes.Where(a => LookupTypes.Contains(a.AttributeType)))
        {
            var outgoing = entity.Relationships.Where(r => string.Equals(r.ReferencingEntity, entity.LogicalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ReferencingAttribute, a.LogicalName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (outgoing.Count > 0 && !outgoing.Any(r => NameOnlyTargets.Contains(r.ReferencedEntity) || relationships.Contains(r)))
            {
                hidden.Add(a.LogicalName);
            }
        }
        if (hidden.Count > 0)
        {
            attributes = attributes.Where(a => !hidden.Contains(a.LogicalName)
                && !hidden.Any(h => string.Equals(a.LogicalName, h + "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.LogicalName, h + "yominame", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.LogicalName, h + "type", StringComparison.OrdinalIgnoreCase))).ToList();
        }

        var manyToMany = entity.ManyToMany.Where(m => snapshot.IsTableOpen(m.Entity1) && snapshot.IsTableOpen(m.Entity2)).ToList();

        return entity with { Attributes = attributes, Relationships = relationships, ManyToMany = manyToMany };
    }

    private async Task<bool> TableUsableAsync(string name, DataPolicySnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.IsTableOpen(name))
        {
            return true;
        }
        return snapshot.StateOfTable(name) != TableState.ClosedByAdmin && await IsImplicitIntersectAsync(name, snapshot, ct);
    }

    private static bool IsAttributeOpen(EntityInfo entity, string attribute, DataPolicySnapshot snapshot)
    {
        if (string.Equals(attribute, entity.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attribute, entity.PrimaryNameAttribute, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var info = entity.FindAttribute(attribute);
        return snapshot.IsFieldOpen(entity.LogicalName, attribute, info?.Label);
    }

    private async Task<bool> IsImplicitIntersectAsync(string name, DataPolicySnapshot snapshot, CancellationToken ct)
    {
        HashSet<string>? set;
        lock (_gate)
        {
            set = _intersectVersion == snapshot.Version ? _intersects : null;
        }
        if (set is null)
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var summary in await _inner.ListEntitiesAsync(ct))
            {
                if (!snapshot.IsTableOpen(summary.LogicalName))
                {
                    continue;
                }
                var info = await _inner.GetEntityAsync(summary.LogicalName, ct);
                if (info is null)
                {
                    continue;
                }
                foreach (var m in info.ManyToMany)
                {
                    if (snapshot.IsTableOpen(m.Entity1) && snapshot.IsTableOpen(m.Entity2))
                    {
                        set.Add(m.IntersectEntity);
                    }
                }
            }
            lock (_gate)
            {
                _intersectVersion = snapshot.Version;
                _intersects = set;
            }
        }
        return set.Contains(name);
    }
}
