using System.Security.Claims;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Web.Identity;

/// <summary>
/// Tests AD group membership checks in fixed and Windows identity modes.
/// </summary>
public class GroupMembershipTests
{
    private static ClaimsPrincipal UserInGroups(params string[] groups)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, @"CONTOSO\someone") };
        claims.AddRange(groups.Select(g => new Claim(ClaimTypes.Role, g)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>
    /// Verifies that fixed mode uses the configured groups ignoring case and never the signed in user.
    /// </summary>
    [Fact]
    public void FixedMode_UsesTheConfiguredGroups_IgnoringCase_AndNeverTheSignedInUser()
    {
        var options = new IdentityOptions { Mode = "Fixed", FixedGroups = new[] { @"CONTOSO\Analysts" } };

        var isMember = GroupMembership.Create(options, UserInGroups(@"CONTOSO\Admins"));

        Assert.True(isMember(@"CONTOSO\Analysts"));
        Assert.True(isMember(@"contoso\ANALYSTS"));
        Assert.False(isMember(@"CONTOSO\Admins"));
    }

    /// <summary>
    /// Verifies that Windows mode uses the signed-in user's groups and ignores fixed groups.
    /// </summary>
    [Fact]
    public void WindowsMode_UsesTheGroupsOfTheSignedInUser_AndIgnoresFixedGroups()
    {
        var options = new IdentityOptions { Mode = "Windows", FixedGroups = new[] { @"CONTOSO\Faked" } };

        var isMember = GroupMembership.Create(options, UserInGroups(@"CONTOSO\Analysts"));

        Assert.True(isMember(@"CONTOSO\Analysts"));
        Assert.False(isMember(@"CONTOSO\Faked"));
        Assert.False(isMember(@"CONTOSO\Other"));
    }

    /// <summary>
    /// Verifies that windows mode without a user is never a member.
    /// </summary>
    [Fact]
    public void WindowsMode_WithoutAUser_IsNeverAMember()
    {
        var isMember = GroupMembership.Create(new IdentityOptions { Mode = "Windows" }, null);

        Assert.False(isMember(@"CONTOSO\Analysts"));
    }

    /// <summary>
    /// Verifies that an unconfigured mode is never a member.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Unconfigured")]
    public void AnUnconfiguredMode_IsNeverAMember(string mode)
    {
        var options = new IdentityOptions { Mode = mode, FixedGroups = new[] { @"CONTOSO\Analysts" } };

        Assert.False(GroupMembership.Create(options, UserInGroups(@"CONTOSO\Analysts"))(@"CONTOSO\Analysts"));
    }

    /// <summary>
    /// Verifies that when the group lookup fails the answer is no.
    /// </summary>
    [Fact]
    public void WhenTheGroupLookupFails_TheAnswerIsNo()
    {
        var isMember = GroupMembership.Create(new IdentityOptions { Mode = "Windows" }, new ThrowingPrincipal());

        Assert.False(isMember(@"CONTOSO\Analysts"));
    }

    private sealed class ThrowingPrincipal : ClaimsPrincipal
    {
        /// <summary>
        /// Fake principal whose role check throws to simulate a failing group lookup.
        /// </summary>
        public override bool IsInRole(string role) => throw new InvalidOperationException("domain controller unreachable");
    }
}
