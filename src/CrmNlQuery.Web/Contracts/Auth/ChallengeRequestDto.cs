namespace CrmNlQuery.Web.Contracts.Auth;

/// <summary>
/// Request to start a CRM-proof sign-in: the browser secret that must accompany the later verification.
/// </summary>
public sealed record ChallengeRequestDto(string? Secret);
