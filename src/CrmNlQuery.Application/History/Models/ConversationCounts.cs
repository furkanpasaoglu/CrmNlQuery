namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds the total and pinned conversation counts shown in the delete-all confirmation.
/// </summary>
public sealed record ConversationCounts(int Total, int Pinned);
