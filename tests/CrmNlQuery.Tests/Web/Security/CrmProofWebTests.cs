using System.Security.Claims;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Tests the web side of the CRM-proof sign-in: the principal built from a token, the sign-in rate limit and how identity modes treat that principal.
/// </summary>
public class CrmProofWebTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FixedResolver : ICallerResolver
    {
        public Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default) => Task.FromResult<Guid?>(windowsAccount == "CONTOSO\\alice" ? Alice : null);
    }

    private static ClaimsPrincipal PrincipalFor(params string[] groups)
        => BearerAuthentication.CreatePrincipal(new AuthTokenClaims("CONTOSO\\alice", Alice, groups, DateTimeOffset.UtcNow.AddHours(1)));

    [Fact]
    public void TheTokenPrincipalCarriesTheAccountAndGroups()
    {
        var principal = PrincipalFor("Sales", "Managers");

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal("CONTOSO\\alice", WindowsAccount.From(principal));
        Assert.True(principal.IsInRole("Sales"));
        Assert.False(principal.IsInRole("Admins"));
    }

    [Fact]
    public void InCrmProofMode_TheGroupsComeFromTheToken_ButNotInOtherModes()
    {
        var principal = PrincipalFor("Sales");

        Assert.True(GroupMembership.Create(new IdentityOptions { Mode = "CrmProof" }, principal)("Sales"));
        Assert.False(GroupMembership.Create(new IdentityOptions { Mode = "CrmProof" }, principal)("Admins"));
        Assert.False(GroupMembership.Create(new IdentityOptions { Mode = "" }, principal)("Sales"));
        Assert.Equal(new[] { "Sales" }, TokenGroups.Names(new IdentityOptions { Mode = "CrmProof" }, principal));
    }

    [Fact]
    public async Task InCrmProofMode_ACallerIsResolvedFromTheSignedInAccount_AndAnonymousIsRefused()
    {
        var identity = new CallerIdentityService(new IdentityOptions { Mode = "CrmProof" }, new FixedResolver());

        var signedIn = await identity.GetCallerAsync(WindowsAccount.From(PrincipalFor()));
        var anonymous = await identity.GetCallerAsync(null);

        Assert.NotNull(signedIn.Caller);
        Assert.Null(anonymous.Caller);
        Assert.NotNull(anonymous.Error);
        Assert.Equal("CONTOSO\\alice", identity.GetAccount("CONTOSO\\alice"));
        Assert.Null(identity.GetAccount(null));
    }

    [Fact]
    public void TheRateLimiterAllowsAFewAttemptsPerMinutePerAddress()
    {
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var limiter = new AuthRateLimiter(3, () => now);

        Assert.True(limiter.Allow("1.1.1.1"));
        Assert.True(limiter.Allow("1.1.1.1"));
        Assert.True(limiter.Allow("1.1.1.1"));
        Assert.False(limiter.Allow("1.1.1.1"));
        Assert.True(limiter.Allow("2.2.2.2"));
        now = now.AddSeconds(61);
        Assert.True(limiter.Allow("1.1.1.1"));
    }
}
