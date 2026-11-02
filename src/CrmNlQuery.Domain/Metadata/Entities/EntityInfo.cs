namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Describes a CRM table with its attributes and relationships.
/// </summary>
public sealed record EntityInfo(
    string LogicalName,
    string EntitySetName,
    string Label,
    string PrimaryIdAttribute,
    string PrimaryNameAttribute,
    IReadOnlyList<AttributeInfo> Attributes,
    IReadOnlyList<RelationshipInfo> Relationships)
{
    public IReadOnlyList<ManyToManyInfo> ManyToMany { get; init; } = Array.Empty<ManyToManyInfo>();

    /// <summary>
    /// Finds an attribute by its logical name.
    /// </summary>
    public AttributeInfo? FindAttribute(string logicalName)
    {
        return Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
    }
}
