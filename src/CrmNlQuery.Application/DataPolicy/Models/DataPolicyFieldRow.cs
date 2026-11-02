namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Represents one field row on the Data access admin page with its state and lock flag.
/// </summary>
public sealed record DataPolicyFieldRow(string Attribute, string Label, string Type, bool Open, string State, bool Locked);
