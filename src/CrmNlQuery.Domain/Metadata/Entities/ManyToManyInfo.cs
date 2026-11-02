namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Describes a many-to-many relationship through an intersect table and the attributes of both ends.
/// </summary>
public sealed record ManyToManyInfo(
    string SchemaName,
    string IntersectEntity,
    string Entity1,
    string Entity1Attribute,
    string Entity2,
    string Entity2Attribute);
