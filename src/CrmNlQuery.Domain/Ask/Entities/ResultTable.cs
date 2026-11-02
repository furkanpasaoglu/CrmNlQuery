namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Holds a query result table with columns, rows and optional hidden per-row record ids.
/// </summary>
public sealed record ResultTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? RowIds = null)
{
    public IReadOnlyList<IReadOnlyList<string>>? Raw { get; init; }
}
