namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for activating or deactivating a user.
/// </summary>
public sealed record UserActiveDto(bool Active);
