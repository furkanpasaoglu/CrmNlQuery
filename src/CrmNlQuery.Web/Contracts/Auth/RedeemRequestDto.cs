namespace CrmNlQuery.Web.Contracts.Auth;

/// <summary>
/// Request to exchange a short hand-over code for a session token in another page of the service.
/// </summary>
public sealed record RedeemRequestDto(string? Code);
