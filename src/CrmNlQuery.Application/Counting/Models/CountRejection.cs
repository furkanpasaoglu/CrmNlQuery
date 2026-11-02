namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Lists reasons a count query cannot be built.
/// </summary>
public enum CountRejection
{
    Unparseable,
    Aggregate,
    Invalid
}
