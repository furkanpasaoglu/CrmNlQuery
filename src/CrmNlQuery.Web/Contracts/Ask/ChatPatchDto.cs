namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for renaming or pinning a chat, leaving unspecified fields unchanged.
/// </summary>
public sealed record ChatPatchDto(string? Title, bool? Pinned);
