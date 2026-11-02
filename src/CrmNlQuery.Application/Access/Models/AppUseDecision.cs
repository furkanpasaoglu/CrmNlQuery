namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Outcome of the app-use check with its decision kind.
/// </summary>
public sealed record AppUseDecision(AppUseDecisionKind Kind);
