namespace CrmNlQuery.Application.Ask.Steps;

/// <summary>
/// Enumeration of results when filling step placeholders, such as filled, no ids or too many ids.
/// </summary>
public enum FillStatus
{
    Filled,
    Empty,
    TooMany,
    Invalid
}
