using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests the guard that refuses fixed identity mode in production.
/// </summary>
public class IdentityGuardTests
{
    private static IdentityOptions Fixed(bool allow = false) => new IdentityOptions { Mode = "Fixed", FixedUser = @"CONTOSO\x", AllowFixedInProduction = allow };

    /// <summary>
    /// Verifies that fixed mode in production refuses to start explaining why.
    /// </summary>
    [Fact]
    public void FixedMode_InProduction_RefusesToStart_ExplainingWhy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IdentityGuard.Validate(Fixed(), "Production"));
        Assert.Contains("Fixed", ex.Message);
        Assert.Contains("AllowFixedInProduction", ex.Message);
    }

    /// <summary>
    /// Verifies that fixed mode in production needs the explicit flag.
    /// </summary>
    [Fact]
    public void FixedMode_InProduction_NeedsTheExplicitFlag()
    {
        IdentityGuard.Validate(Fixed(allow: true), "Production");
    }

    /// <summary>
    /// Verifies that fixed mode in development is fine.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void FixedMode_InDevelopment_IsFine(string environment)
    {
        IdentityGuard.Validate(Fixed(), environment);
    }

    /// <summary>
    /// Verifies that other modes are not affected.
    /// </summary>
    [Theory]
    [InlineData("Windows")]
    [InlineData("")]
    public void OtherModes_AreNotAffected(string mode)
    {
        IdentityGuard.Validate(new IdentityOptions { Mode = mode }, "Production");
    }

    /// <summary>
    /// Verifies that mode is case insensitive.
    /// </summary>
    [Fact]
    public void ModeIsCaseInsensitive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            IdentityGuard.Validate(new IdentityOptions { Mode = "fIxEd" }, "Production"));
    }

    /// <summary>
    /// Verifies that an unknown environment name counts as production.
    /// </summary>
    [Fact]
    public void AnUnknownEnvironmentName_CountsAsProduction()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityGuard.Validate(Fixed(), "Staging"));
    }
}
