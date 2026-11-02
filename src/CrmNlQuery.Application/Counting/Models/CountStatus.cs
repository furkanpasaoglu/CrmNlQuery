namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Lists the outcomes of counting a truncated result.
/// </summary>
public enum CountStatus
{
    Ok,
    NotFound,
    NotEligible,
    Unavailable
}
