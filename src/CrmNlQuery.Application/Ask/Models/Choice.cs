namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Selectable clarification option pairing a display label with the value sent back when chosen.
/// </summary>
public sealed record Choice(string Label, string Value);
