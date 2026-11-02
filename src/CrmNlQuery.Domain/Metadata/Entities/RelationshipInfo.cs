namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Describes a one-to-many relationship between two CRM tables.
/// </summary>
public sealed record RelationshipInfo(
    string SchemaName,
    string ReferencingEntity,
    string ReferencingAttribute,
    string ReferencedEntity,
    string ReferencedAttribute);
