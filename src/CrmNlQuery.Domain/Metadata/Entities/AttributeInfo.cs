namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Describes one attribute of a CRM table.
/// </summary>
public sealed record AttributeInfo(string LogicalName, string Label, string AttributeType, IReadOnlyList<OptionInfo> Options);
