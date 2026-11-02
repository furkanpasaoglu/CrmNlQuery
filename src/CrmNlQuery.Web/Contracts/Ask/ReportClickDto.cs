namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request body for counting a report card click.
/// </summary>
public sealed record ReportClickDto(string? ResultId, string? ReportId);
