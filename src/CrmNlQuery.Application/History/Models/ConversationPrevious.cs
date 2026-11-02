using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds the loaded previous query and its history entry for a follow-up question.
/// </summary>
public sealed record ConversationPrevious(PreviousQuery Query, string ResultId, string? ConversationId = null);
