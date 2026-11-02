namespace CrmNlQuery.Application.CrmViews.Models;

/// <summary>
/// Lists the reasons a view cannot be built from a query.
/// </summary>
public enum ViewRejection
{
    Unparseable,
    Aggregate,
    Invalid,
    NoColumns,
    OneToManyColumns,
    NestedColumns
}
