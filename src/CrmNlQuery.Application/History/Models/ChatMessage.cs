namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Represents one chat message whose id is also the result id used to open it.
/// </summary>
public sealed record ChatMessage(string Id, string Question, string Entity, DateTimeOffset CreatedAt);
