using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.DataPolicy.Models;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Represents the allowlist state at one moment: explicit administrator decisions, starter tables and the sensitive-name rule.
/// </summary>
public sealed class DataPolicySnapshot
{
    private readonly Dictionary<string, bool> _tables = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _fields = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _defaultTables;
    private readonly IReadOnlyList<DataPolicyRule> _rules;

    public bool Enabled { get; }

    public string Version { get; }

    public IReadOnlyList<DataPolicyRule> Rules => _rules;

    public IReadOnlyCollection<string> DefaultTables => _defaultTables;

    public DataPolicySnapshot(IReadOnlyList<DataPolicyRule> rules, IReadOnlyCollection<string> defaultTables, bool enabled = true)
    {
        _rules = rules;
        _defaultTables = new HashSet<string>(defaultTables, StringComparer.OrdinalIgnoreCase);
        Enabled = enabled;
        foreach (var rule in rules)
        {
            if (rule.Kind == DataPolicyRule.Table)
            {
                _tables[rule.Entity] = rule.Allowed;
            }
            else if (rule.Kind == DataPolicyRule.Field && rule.Attribute is not null)
            {
                _fields[rule.Entity + "|" + rule.Attribute] = rule.Allowed;
            }
        }
        Version = ComputeVersion(rules, _defaultTables, enabled);
    }

    /// <summary>
    /// Returns a copy of the snapshot with the whole policy switched on or off.
    /// </summary>
    public DataPolicySnapshot WithEnabled(bool enabled)
    {
        return enabled == Enabled ? this : new DataPolicySnapshot(_rules, _defaultTables, enabled);
    }

    /// <summary>
    /// Returns the state of a table in the policy.
    /// </summary>
    public TableState StateOfTable(string entity)
    {
        if (_tables.TryGetValue(entity, out var allowed))
        {
            return allowed ? TableState.OpenByAdmin : TableState.ClosedByAdmin;
        }
        return _defaultTables.Contains(entity) ? TableState.Open : TableState.ClosedByDefault;
    }

    /// <summary>
    /// Tells whether a table is open.
    /// </summary>
    public bool IsTableOpen(string entity)
    {
        var state = StateOfTable(entity);
        return state == TableState.Open || state == TableState.OpenByAdmin;
    }

    /// <summary>
    /// Returns the state of a field, applying administrator decisions and the sensitive-name rule.
    /// </summary>
    public FieldState StateOfField(string entity, string attribute, string? label)
    {
        if (_fields.TryGetValue(entity + "|" + attribute, out var allowed))
        {
            return allowed ? FieldState.OpenByAdmin : FieldState.ClosedByAdmin;
        }
        return SensitiveNames.IsSensitive(attribute, label) ? FieldState.ClosedAutomatically : FieldState.Open;
    }

    /// <summary>
    /// Tells whether a field is open.
    /// </summary>
    public bool IsFieldOpen(string entity, string attribute, string? label)
    {
        var state = StateOfField(entity, attribute, label);
        return state == FieldState.Open || state == FieldState.OpenByAdmin;
    }

    private static string ComputeVersion(IReadOnlyList<DataPolicyRule> rules, HashSet<string> defaults, bool enabled)
    {
        var text = new StringBuilder();
        text.Append(enabled ? "on;" : "off;");
        foreach (var d in defaults.Select(x => x.ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal))
        {
            text.Append("d:").Append(d).Append(';');
        }
        foreach (var r in rules.Select(x => $"{x.Kind}|{x.Entity.ToLowerInvariant()}|{x.Attribute?.ToLowerInvariant()}|{(x.Allowed ? 1 : 0)}").OrderBy(x => x, StringComparer.Ordinal))
        {
            text.Append("r:").Append(r).Append(';');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }
}
