using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Composition;
using CrmNlQuery.Web.Contracts.Auth;
using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Web.Endpoints.Auth;

/// <summary>
/// Maps the CRM-proof sign-in endpoints; they exist only when Identity:Mode is CrmProof.
/// </summary>
public static class AuthEndpoints
{
    private static readonly AuthRateLimiter Limiter = new AuthRateLimiter(30);

    /// <summary>
    /// Registers the challenge and verify endpoints.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services, IReadOnlyList<string> allowedOrigins)
    {
        var login = services.CrmLogin;
        if (login is null)
        {
            return;
        }

        app.MapGet("/api/auth/origins", () => Results.Json(new { origins = allowedOrigins }))
            .Public("Lists the CRM origins allowed to hand a session over to a page of the service; the pages use it to accept a hand-over code only from those origins.");

        app.MapPost("/api/auth/challenge", async (ChallengeRequestDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!Limiter.Allow(Address(http)))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            var challenge = await login.CreateChallengeAsync(dto.Secret, ct);
            return challenge is null
                ? Results.Json(new { error = "invalid_secret" }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(new { nonce = challenge.Nonce, subject = challenge.Subject, expiresInSeconds = challenge.ExpiresInSeconds });
        }).RequireCors("crm-widget").Public("Starts the CRM-proof sign-in; the caller is not signed in yet and receives only a one-time code that proves nothing by itself.");

        app.MapPost("/api/auth/handoff", (HttpContext http) =>
        {
            var session = BearerAuthentication.ReadClaims(http.User);
            if (session is null || services.AuthTokens is null)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }
            return Results.Json(new { code = services.AuthTokens.IssueHandoff(session, TimeSpan.FromSeconds(30)) });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/auth/redeem", (RedeemRequestDto dto, HttpContext http) =>
        {
            if (!Limiter.Allow(Address(http)))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            var claims = services.AuthTokens?.ValidateHandoff(dto.Code);
            if (claims is null || services.AuthTokens is null)
            {
                return Results.Json(new { error = "invalid_code" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            return Results.Json(new { token = services.AuthTokens.IssueSession(claims), account = claims.Account, expiresAt = claims.ExpiresAt });
        }).Public("Exchanges a 30-second hand-over code made from a signed-in session for a session token; access is decided by the signed, unexpired code.");

        app.MapPost("/api/auth/verify", async (VerifyRequestDto dto, HttpContext http, CancellationToken ct) =>
        {
            if (!Limiter.Allow(Address(http)))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            var result = await login.VerifyAsync(dto.Nonce, dto.Secret, ct);
            if (!result.Ok)
            {
                var status = result.Error == LoginError.CrmUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized;
                return Results.Json(new { error = ErrorCode(result.Error) }, statusCode: status);
            }
            return Results.Json(new { token = result.Token, account = result.Account, expiresAt = result.ExpiresAt });
        }).RequireCors("crm-widget").Public("Finishes the CRM-proof sign-in; access is decided by the proof the CRM holds for the one-time code and the browser secret.");
    }

    private static string Address(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string ErrorCode(LoginError error) => error switch
    {
        LoginError.InvalidChallenge => "invalid_challenge",
        LoginError.NoProof => "no_proof",
        LoginError.Impersonated => "impersonated",
        LoginError.RecordModified => "record_modified",
        LoginError.NotActiveUser => "not_active_user",
        LoginError.CrmUnavailable => "crm_unavailable",
        _ => "refused",
    };
}
