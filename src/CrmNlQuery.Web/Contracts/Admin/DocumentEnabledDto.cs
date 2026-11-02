namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body for enabling or disabling a knowledge document.
/// </summary>
public sealed record DocumentEnabledDto(bool Enabled);
