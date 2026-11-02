namespace CrmNlQuery.Application.Summary.Models;

/// <summary>
/// One record line in a summary section with main text, detail text and record link data.
/// </summary>
public sealed record SummaryItem(string Text, string? Detail, string? Entity = null, string? Id = null);
