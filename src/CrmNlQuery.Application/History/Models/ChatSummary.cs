namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Summarizes a chat for lists without FetchXML or rows, only the fields needed for display.
/// </summary>
public sealed record ChatSummary(string Id, string Title, bool Pinned, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
