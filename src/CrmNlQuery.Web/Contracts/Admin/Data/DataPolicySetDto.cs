namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Request body for opening, closing or resetting a data policy decision.
/// </summary>
public sealed record DataPolicySetDto(bool? Allowed);
