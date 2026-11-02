namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Enumeration of access-operation failure kinds, including ConfirmationRequired for risky changes that must be resent with explicit confirmation (HTTP 409).
/// </summary>
public enum AccessError { None, Forbidden, NotFound, Invalid, Conflict, Unavailable, ConfirmationRequired }
