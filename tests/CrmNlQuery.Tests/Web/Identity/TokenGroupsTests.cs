using System.Security.Claims;
using CrmNlQuery.Web.Identity;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Web.Identity;

/// <summary>
/// Tests token group names: no error leakage, SID text fallback, stable order and limits.
/// </summary>
public class TokenGroupsTests
{
    private static ClaimsPrincipal PrincipalWith(params string[] groupSids)
        => new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, @"LAB\u") }.Concat(groupSids.Select(s => new Claim(ClaimTypes.GroupSid, s))), "test"));

    private static IdentityOptions Windows() => new IdentityOptions { Mode = "Windows" };

    /// <summary>
    /// Verifies that fixed mode lists the configured groups sorted and without blanks.
    /// </summary>
    [Fact]
    public void FixedMode_ListsTheConfiguredGroups_SortedAndWithoutBlanks()
    {
        var options = new IdentityOptions { Mode = "Fixed", FixedGroups = new[] { @"LAB\Sales", " ", @"LAB\Everyone", @"lab\sales" } };

        Assert.Equal(new[] { @"LAB\Everyone", @"LAB\Sales" }, TokenGroups.Names(options, null));
    }

    /// <summary>
    /// Verifies that windows mode translates known sids and keeps the sid text when it cannot be translated.
    /// </summary>
    [Fact]
    public void WindowsMode_TranslatesKnownSids_AndKeepsTheSidTextWhenItCannotBeTranslated()
    {
        var names = TokenGroups.Names(Windows(), PrincipalWith("S-1-1-0", "S-1-5-21-111-222-333-513"));

        Assert.Equal(2, names.Count);
        Assert.Contains("Everyone", names[0]);
        Assert.Equal("S-1-5-21-111-222-333-513", names[1]);
    }

    /// <summary>
    /// Verifies that a malformed sid claim is ignored not thrown.
    /// </summary>
    [Fact]
    public void AMalformedSidClaim_IsIgnored_NotThrown()
    {
        var names = TokenGroups.Names(Windows(), PrincipalWith("not-a-sid", "S-1-1-0"));

        Assert.Single(names);
    }

    /// <summary>
    /// Verifies that the list is limited.
    /// </summary>
    [Fact]
    public void TheListIsLimited()
    {
        var sids = Enumerable.Range(1, 60).Select(i => "S-1-5-21-1-2-3-" + (1000 + i)).ToArray();

        Assert.Equal(40, TokenGroups.Names(Windows(), PrincipalWith(sids), 40).Count);
        Assert.Equal(60, TokenGroups.Names(Windows(), PrincipalWith(sids)).Count);
    }

    /// <summary>
    /// Verifies that any other mode or no user gives an empty list.
    /// </summary>
    [Theory]
    [InlineData("Unconfigured")]
    [InlineData("")]
    public void AnyOtherMode_OrNoUser_GivesAnEmptyList(string mode)
    {
        Assert.Empty(TokenGroups.Names(new IdentityOptions { Mode = mode }, PrincipalWith("S-1-1-0")));
        Assert.Empty(TokenGroups.Names(Windows(), null));
    }

    /// <summary>
    /// Verifies that describe joins at most forty names and is null when there are none.
    /// </summary>
    [Fact]
    public void Describe_JoinsAtMostFortyNames_AndIsNullWhenThereAreNone()
    {
        var sids = Enumerable.Range(1, 60).Select(i => "S-1-5-21-1-2-3-" + (1000 + i)).ToArray();

        var text = TokenGroups.Describe(Windows(), PrincipalWith(sids));

        Assert.Equal(40, text!.Split(", ").Length);
        Assert.Null(TokenGroups.Describe(Windows(), PrincipalWith()));
    }
}
