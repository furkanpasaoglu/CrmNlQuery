namespace CrmNlQuery.Web.Contracts.Auth;

/// <summary>
/// Request to finish a CRM-proof sign-in: the nonce the browser proved in the CRM and the browser secret.
/// </summary>
public sealed record VerifyRequestDto(string? Nonce, string? Secret);
