namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Why a CRM-proof sign-in was refused; the code is shown to the client and logged, never the nonce or token.
/// </summary>
public enum LoginError
{
    None,
    InvalidChallenge,
    NoProof,
    Impersonated,
    RecordModified,
    NotActiveUser,
    CrmUnavailable,
}
