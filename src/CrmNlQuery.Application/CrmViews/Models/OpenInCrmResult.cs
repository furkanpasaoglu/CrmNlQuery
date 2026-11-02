namespace CrmNlQuery.Application.CrmViews.Models;

/// <summary>
/// Carries the status and optional CRM address of an Open in CRM request.
/// </summary>
public sealed record OpenInCrmResult(OpenInCrmStatus Status, string? Url = null, string? Message = null);
