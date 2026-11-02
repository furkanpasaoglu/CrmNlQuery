using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Holds validation helpers for data policy rules.
/// </summary>
public static class DataPolicyRules
{
    /// <summary>
    /// Lowercases a data policy rule and rejects an invalid one.
    /// </summary>
    public static DataPolicyRule Normalize(DataPolicyRule rule)
    {
        var kind = (rule.Kind ?? "").ToLowerInvariant();
        var entity = (rule.Entity ?? "").Trim().ToLowerInvariant();
        var attribute = string.IsNullOrWhiteSpace(rule.Attribute) ? null : rule.Attribute.Trim().ToLowerInvariant();
        if (kind != DataPolicyRule.Table && kind != DataPolicyRule.Field)
        {
            throw new ArgumentException("Unknown rule kind.", nameof(rule));
        }
        if (entity.Length == 0)
        {
            throw new ArgumentException("A rule needs a table name.", nameof(rule));
        }
        if (kind == DataPolicyRule.Field && attribute is null)
        {
            throw new ArgumentException("A field rule needs a field name.", nameof(rule));
        }
        if (kind == DataPolicyRule.Table && attribute is not null)
        {
            throw new ArgumentException("A table rule has no field name.", nameof(rule));
        }
        return rule with { Kind = kind, Entity = entity, Attribute = attribute };
    }
}
