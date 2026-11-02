namespace CrmNlQuery.Application.Explain.Models;

/// <summary>
/// Represents a value in a query explanation: a raw value that never holds ids or GUIDs, and a display label.
/// </summary>
public sealed record ExplainValue(string? Value, string? Label);
