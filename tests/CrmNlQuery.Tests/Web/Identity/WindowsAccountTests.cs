using System.Security.Claims;
using System.Security.Principal;
using CrmNlQuery.Web.Identity;

namespace CrmNlQuery.Tests.Web.Identity;

/// <summary>
/// Tests extraction of the Windows account name from a user principal.
/// </summary>
public class WindowsAccountTests
{
    /// <summary>
    /// Verifies that no user gives null.
    /// </summary>
    [Fact]
    public void NoUser_GivesNull()
    {
        Assert.Null(WindowsAccount.From(null));
    }

    /// <summary>
    /// Verifies that an anonymous user gives null.
    /// </summary>
    [Fact]
    public void AnonymousUser_GivesNull()
    {
        Assert.Null(WindowsAccount.From(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>
    /// Verifies that a signed-in user gives the account name.
    /// </summary>
    [Fact]
    public void SignedInUser_GivesTheAccountName()
    {
        var principal = new ClaimsPrincipal(new GenericIdentity(@"CONTOSO\testuser_a", "Negotiate"));
        Assert.Equal(@"CONTOSO\testuser_a", WindowsAccount.From(principal));
    }

    /// <summary>
    /// Verifies that a signed-in user with a blank name gives null.
    /// </summary>
    [Fact]
    public void SignedInButBlankName_GivesNull()
    {
        var principal = new ClaimsPrincipal(new GenericIdentity("   ", "Negotiate"));
        Assert.Null(WindowsAccount.From(principal));
    }
}
