namespace CrmNlQuery.Application.Suggestions.Models;

/// <summary>
/// Holds how many times a suggestion kind was shown and clicked, with no question text, field, value or user.
/// </summary>
public sealed record SuggestionStat(string Kind, int Shown, int Clicked);
