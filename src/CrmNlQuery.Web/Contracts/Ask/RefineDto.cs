namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for refining a result with extra text.
/// </summary>
public sealed record RefineDto(string? Text, Dictionary<string, string>? Answers);
