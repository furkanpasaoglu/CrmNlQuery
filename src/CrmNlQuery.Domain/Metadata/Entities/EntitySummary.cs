namespace CrmNlQuery.Domain.Metadata.Entities;

/// <summary>
/// Short description of a CRM table: logical name, label and entity set name.
/// </summary>
public sealed record EntitySummary(string LogicalName, string EntitySetName, string Label);
