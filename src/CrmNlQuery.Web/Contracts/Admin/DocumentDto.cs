namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for adding or updating a knowledge document.
/// </summary>
public sealed record DocumentDto(string? Title, string? Body);
