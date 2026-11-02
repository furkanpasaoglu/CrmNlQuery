namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Lists whether a field is open or closed and why, in the data policy.
/// </summary>
public enum FieldState { Open, OpenByAdmin, ClosedByAdmin, ClosedAutomatically }
