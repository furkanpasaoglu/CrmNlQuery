using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// In-memory data policy store used when no database is configured and in tests.
/// </summary>
public sealed class MemoryDataPolicyStore : IDataPolicyStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, DataPolicyRule> _rules = new Dictionary<string, DataPolicyRule>();

    public bool Persistent => false;

    /// <summary>
    /// Lists data-policy rules from memory.
    /// </summary>
    public Task<IReadOnlyList<DataPolicyRule>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DataPolicyRule>>(_rules.Values.OrderBy(r => r.Entity, StringComparer.Ordinal).ThenBy(r => r.Attribute, StringComparer.Ordinal).ToList());
        }
    }

    /// <summary>
    /// Stores or replaces a data-policy rule in memory.
    /// </summary>
    public Task SetAsync(DataPolicyRule rule, CancellationToken ct = default)
    {
        var clean = DataPolicyRules.Normalize(rule);
        lock (_gate)
        {
            _rules[Key(clean.Kind, clean.Entity, clean.Attribute)] = clean;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a table or field rule from memory and reports whether one existed.
    /// </summary>
    public Task<bool> RemoveAsync(string kind, string entity, string? attribute, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_rules.Remove(Key(kind, entity, attribute)));
        }
    }

    private static string Key(string kind, string entity, string? attribute)
        => kind.ToLowerInvariant() + "|" + entity.ToLowerInvariant() + "|" + (attribute ?? "").ToLowerInvariant();
}
