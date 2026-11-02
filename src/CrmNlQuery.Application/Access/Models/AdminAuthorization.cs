using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Holds the admin authorization decision, the principal and the denial reason, which is audited but not shown to the user.
/// </summary>
public sealed record AdminAuthorization(AdminDecision Decision, AccessPrincipal? Principal, string? Reason = null);
