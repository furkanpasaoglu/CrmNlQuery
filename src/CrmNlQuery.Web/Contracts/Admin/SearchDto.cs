namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body carrying a question to search the knowledge base.
/// </summary>
public sealed record SearchDto(string? Question);
