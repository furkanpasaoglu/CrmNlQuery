namespace CrmNlQuery.Application.CrmViews.Models;

/// <summary>
/// Lists the outcomes of an Open in CRM request.
/// </summary>
public enum OpenInCrmStatus
{
    Opened,
    Disabled,
    NotFound,
    NotEligible,
    PermissionDenied,
    Failed
}
