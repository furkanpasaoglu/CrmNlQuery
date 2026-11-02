namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Lists whether a table is open or closed and why, in the data policy.
/// </summary>
public enum TableState { Open, OpenByAdmin, ClosedByAdmin, ClosedByDefault }
