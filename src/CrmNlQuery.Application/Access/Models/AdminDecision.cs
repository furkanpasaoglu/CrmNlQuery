namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Lists the possible outcomes of an admin authorization decision.
/// </summary>
public enum AdminDecision
{
    Allowed,
    NotFound,
    Unavailable,
}
