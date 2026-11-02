namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for request.
/// </summary>
public sealed record DesignRequest(string Text, string? Prefix = null);
