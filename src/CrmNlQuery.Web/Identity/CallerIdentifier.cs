using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Web.Contracts.Ask;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Web.Identity;

/// <summary>
/// Identifies who is asking; if unknown, no data is read and the service account is never used.
/// </summary>
public sealed class CallerIdentifier
{
    private readonly CallerIdentityService _identity;

    public CallerIdentifier(CallerIdentityService identity)
    {
        _identity = identity;
    }

    /// <summary>
    /// Resolves the requesting caller from the HTTP context or reports that identification failed.
    /// </summary>
    public async Task<(Caller? Caller, IResult? Refusal)> IdentifyAsync(HttpContext http, CancellationToken ct)
    {
        CallerResult caller;
        try
        {
            caller = await _identity.GetCallerAsync(WindowsAccount.From(http.User), ct);
        }
        catch (CrmException ex)
        {
            return (null, Results.Json(OutcomeDto.From(new FailureOutcome(UserMessages.ForCrm(ex))), statusCode: 503));
        }
        if (caller.Error is not null || caller.Caller is null)
        {
            return (null, Results.Json(OutcomeDto.From(new FailureOutcome(caller.Error ?? "Unknown user.")), statusCode: 401));
        }
        return (caller.Caller, null);
    }
}
