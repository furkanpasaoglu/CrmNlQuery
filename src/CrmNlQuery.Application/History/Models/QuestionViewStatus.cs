namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Enumeration of outcomes when an administrator requests viewable question texts, such as allowed or feature disabled.
/// </summary>
public enum QuestionViewStatus
{
    Ok,
    NotEnabled,
    NotFound,
}
