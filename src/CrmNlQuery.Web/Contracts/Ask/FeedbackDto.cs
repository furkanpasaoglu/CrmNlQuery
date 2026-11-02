namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for a thumbs vote with reason and optional sharing.
/// </summary>
public sealed record FeedbackDto(string? Rating, string? Reason, bool? Share);
