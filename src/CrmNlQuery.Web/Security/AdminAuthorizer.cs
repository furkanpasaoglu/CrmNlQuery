using CrmNlQuery.Web.Identity;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Authorization gate for admin endpoints: no identity gives 401 and unregistered or unauthorized users get 404 so the panel's existence is hidden.
/// </summary>
public sealed class AdminAuthorizer
{
    private readonly CallerIdentityService _identity;
    private readonly IdentityOptions _options;
    private readonly AdminAccess _access;
    private readonly AccessService _audit;
    private readonly ILogger _logger;

    public AdminAuthorizer(CallerIdentityService identity, IdentityOptions options, AdminAccess access, AccessService audit, ILogger logger)
    {
        _identity = identity;
        _options = options;
        _access = access;
        _audit = audit;
        _logger = logger;
    }

    private static string EndpointName(HttpContext http) => EndpointNames.For(http);

    /// <summary>
    /// Authorizes an admin request and returns the principal or a refusal result.
    /// </summary>
    public async Task<(AccessPrincipal? Principal, IResult? Refusal)> AuthorizeAsync(HttpContext http, string? permission, CancellationToken ct)
    {
        var account = _identity.GetAccount(WindowsAccount.From(http.User));
        if (account is null)
        {
            _logger.LogWarning("Admin endpoint {Endpoint} was called without a Windows identity.", EndpointName(http));
            return (null, Results.Json(OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.SignInRequired))), statusCode: 401));
        }
        var decision = await _access.AuthorizeAsync(account, permission, ct, GroupMembership.Create(_options, http.User));
        switch (decision.Decision)
        {
            case AdminDecision.Allowed:
                return (decision.Principal, null);
            case AdminDecision.Unavailable:
                _logger.LogWarning("Admin endpoint {Endpoint}: the access store could not be reached, so the request was refused.", EndpointName(http));
                return (null, Results.Json(
                    OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.RightsUnavailable), "admin_unavailable")), statusCode: 503));
            default:
                try
                {
                    await _audit.RecordDeniedAsync(account, EndpointName(http), permission, decision.Reason ?? AdminAccess.NoAccess, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not write the denied-access audit record for {Endpoint}.", EndpointName(http));
                }
                return (null, Results.NotFound());
        }
    }
}
