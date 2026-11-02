namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Represents one table row on the Data access admin page with its state and counts.
/// </summary>
public sealed record DataPolicyTableRow(string Entity, string Label, bool Open, string Source, int? AutoClosedFields, int? AdminClosedFields);
