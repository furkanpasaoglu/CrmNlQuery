using CrmNlQuery.Composition;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Web.Security;

/// <summary>
/// Extension methods that mark endpoints as caller-required, admin-required or public and read the resolved identity.
/// </summary>
public static class EndpointMarkers
{
    private const string CallerKey = "nlquery.caller";
    private const string AdminKey = "nlquery.admin";

    /// <summary>
    /// Marks an endpoint as requiring a resolved CRM caller.
    /// </summary>
    public static RouteHandlerBuilder RequireCaller(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new RequireCallerMarker());
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices.GetRequiredService<AppServices>();
            var account = services.Identity.GetAccount(WindowsAccount.From(http.User));
            if (account is not null)
            {
                var decision = await services.AppUse.EvaluateAsync(account, GroupMembership.Create(services.IdentityOptions, http.User), EndpointNames.For(http),
                    () => TokenGroups.Describe(services.IdentityOptions, http.User), http.RequestAborted);
                if (decision.Kind == AppUseDecisionKind.Denied)
                {
                    return Results.Json(OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.NotAllowed), "not_allowed")), statusCode: StatusCodes.Status403Forbidden);
                }
                if (decision.Kind == AppUseDecisionKind.Unavailable)
                {
                    return Results.Json(OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.RightsUnavailable), "access_unavailable")), statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }

            var identifier = http.RequestServices.GetRequiredService<CallerIdentifier>();
            var (caller, refusal) = await identifier.IdentifyAsync(http, http.RequestAborted);
            if (refusal is not null || caller is null)
            {
                return refusal ?? Results.Unauthorized();
            }
            http.Items[CallerKey] = caller;
            return await next(context);
        });
        return builder;
    }

    /// <summary>
    /// Marks an endpoint as requiring admin access, optionally with a specific permission.
    /// </summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder, string? permission)
    {
        builder.WithMetadata(new RequireAdminMarker(permission));
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var authorizer = http.RequestServices.GetRequiredService<AdminAuthorizer>();
            var (principal, refusal) = await authorizer.AuthorizeAsync(http, permission, http.RequestAborted);
            if (refusal is not null || principal is null)
            {
                return refusal ?? Results.NotFound();
            }
            http.Items[AdminKey] = principal;
            return await next(context);
        });
        return builder;
    }

    /// <summary>
    /// Marks an endpoint as intentionally public with a stated reason.
    /// </summary>
    public static RouteHandlerBuilder Public(this RouteHandlerBuilder builder, string reason)
    {
        return builder.WithMetadata(new PublicMarker(reason));
    }

    /// <summary>
    /// Returns the caller identity set by the RequireCaller filter, throwing if the endpoint was not marked.
    /// </summary>
    public static Caller Caller(this HttpContext http)
    {
        return http.Items[CallerKey] as Caller ?? throw new InvalidOperationException("This endpoint is not marked with RequireCaller().");
    }

    /// <summary>
    /// Returns the admin access result set by the RequireAdmin filter for the current request.
    /// </summary>
    public static AccessPrincipal Admin(this HttpContext http)
    {
        return http.Items[AdminKey] as AccessPrincipal ?? throw new InvalidOperationException("This endpoint is not marked with RequireAdmin().");
    }
}
