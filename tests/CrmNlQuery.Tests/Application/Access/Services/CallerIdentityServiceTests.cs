using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Holds tests for resolving the calling CRM user from a Windows account.
/// </summary>
public class CallerIdentityServiceTests
{
    private sealed class FakeResolver : ICallerResolver
    {
        private readonly Dictionary<string, Guid> _known;
        public List<string> Asked { get; } = new List<string>();
        public FakeResolver(Dictionary<string, Guid>? known = null) { _known = known ?? new Dictionary<string, Guid>(); }
        /// <summary>
        /// Test resolver that returns a canned user id for an account.
        /// </summary>
        public Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default)
        {
            Asked.Add(windowsAccount);
            return Task.FromResult<Guid?>(_known.TryGetValue(windowsAccount, out var id) ? id : null);
        }
    }

    private static CallerIdentityService Service(IdentityOptions options, FakeResolver? resolver = null)
        => new CallerIdentityService(options, resolver ?? new FakeResolver());

    /// <summary>
    /// Verifies that not configured is refused fail closed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Whatever")]
    public async Task NotConfigured_IsRefused_FailClosed(string? mode)
    {
        var resolver = new FakeResolver();
        var result = await Service(new IdentityOptions { Mode = mode ?? "" }, resolver).GetCallerAsync();
        Assert.Null(result.Caller?.UserId);
        Assert.NotNull(result.Error);
        Assert.Empty(resolver.Asked);
    }

    /// <summary>
    /// Verifies that fixed resolves the configured user.
    /// </summary>
    [Fact]
    public async Task Fixed_ResolvesTheConfiguredUser()
    {
        var id = Guid.NewGuid();
        var resolver = new FakeResolver(new Dictionary<string, Guid> { [@"CONTOSO\testuser_a"] = id });
        var result = await Service(new IdentityOptions { Mode = "Fixed", FixedUser = @"CONTOSO\testuser_a" }, resolver).GetCallerAsync();
        Assert.Equal(id, result.Caller?.UserId);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Verifies that mode is case insensitive.
    /// </summary>
    [Fact]
    public async Task Mode_IsCaseInsensitive()
    {
        var id = Guid.NewGuid();
        var resolver = new FakeResolver(new Dictionary<string, Guid> { [@"CONTOSO\a"] = id });
        var result = await Service(new IdentityOptions { Mode = "fixed", FixedUser = @"CONTOSO\a" }, resolver).GetCallerAsync();
        Assert.Equal(id, result.Caller?.UserId);
    }

    /// <summary>
    /// Verifies that fixed without aUser is refused.
    /// </summary>
    [Fact]
    public async Task Fixed_WithoutAUser_IsRefused()
    {
        var result = await Service(new IdentityOptions { Mode = "Fixed", FixedUser = "" }).GetCallerAsync();
        Assert.Null(result.Caller?.UserId);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// Verifies that fixed user not in CRM is refused with the account named.
    /// </summary>
    [Fact]
    public async Task Fixed_UserNotInCrm_IsRefused_WithTheAccountNamed()
    {
        var result = await Service(new IdentityOptions { Mode = "Fixed", FixedUser = @"CONTOSO\ghost" }).GetCallerAsync();
        Assert.Null(result.Caller?.UserId);
        Assert.Contains(@"CONTOSO\ghost", result.Error);
    }

    /// <summary>
    /// Verifies that windows mode resolves the signed in account.
    /// </summary>
    [Fact]
    public async Task WindowsMode_ResolvesTheSignedInAccount()
    {
        var id = Guid.NewGuid();
        var resolver = new FakeResolver(new Dictionary<string, Guid> { [@"CONTOSO\testuser_a"] = id });
        var result = await Service(new IdentityOptions { Mode = "Windows" }, resolver).GetCallerAsync(@"CONTOSO\testuser_a");
        Assert.Equal(id, result.Caller?.UserId);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Verifies that windows mode without aSigned in user is refused and never falls back.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WindowsMode_WithoutASignedInUser_IsRefused_AndNeverFallsBack(string? account)
    {
        var resolver = new FakeResolver();
        var result = await Service(new IdentityOptions { Mode = "Windows", FixedUser = @"CONTOSO\testuser_a" }, resolver).GetCallerAsync(account);
        Assert.Null(result.Caller?.UserId);
        Assert.NotNull(result.Error);
        Assert.Empty(resolver.Asked);
    }

    /// <summary>
    /// Verifies that windows mode account not in CRM is refused with the account named.
    /// </summary>
    [Fact]
    public async Task WindowsMode_AccountNotInCrm_IsRefused_WithTheAccountNamed()
    {
        var result = await Service(new IdentityOptions { Mode = "Windows" }).GetCallerAsync(@"OTHER\someone");
        Assert.Null(result.Caller?.UserId);
        Assert.Contains(@"OTHER\someone", result.Error);
    }

    /// <summary>
    /// Verifies that windows mode ignores fixed user.
    /// </summary>
    [Fact]
    public async Task WindowsMode_IgnoresFixedUser()
    {
        var fixedId = Guid.NewGuid();
        var realId = Guid.NewGuid();
        var resolver = new FakeResolver(new Dictionary<string, Guid> { [@"CONTOSO\fixed"] = fixedId, [@"CONTOSO\real"] = realId });
        var result = await Service(new IdentityOptions { Mode = "Windows", FixedUser = @"CONTOSO\fixed" }, resolver).GetCallerAsync(@"CONTOSO\real");
        Assert.Equal(realId, result.Caller?.UserId);
    }

    /// <summary>
    /// Verifies that fixed mode ignores the signed in account.
    /// </summary>
    [Fact]
    public async Task FixedMode_IgnoresTheSignedInAccount()
    {
        var fixedId = Guid.NewGuid();
        var resolver = new FakeResolver(new Dictionary<string, Guid> { [@"CONTOSO\fixed"] = fixedId, [@"CONTOSO\real"] = Guid.NewGuid() });
        var result = await Service(new IdentityOptions { Mode = "Fixed", FixedUser = @"CONTOSO\fixed" }, resolver).GetCallerAsync(@"CONTOSO\real");
        Assert.Equal(fixedId, result.Caller?.UserId);
    }

    /// <summary>
    /// Verifies that get account follows the mode without asking CRM.
    /// </summary>
    [Theory]
    [InlineData("Fixed", @"CONTOSO\fixed", @"CONTOSO\real", @"CONTOSO\fixed")]
    [InlineData("Windows", @"CONTOSO\fixed", @"CONTOSO\real", @"CONTOSO\real")]
    [InlineData("windows", "", @"  CONTOSO\real  ", @"CONTOSO\real")]
    public void GetAccount_FollowsTheMode_WithoutAskingCrm(string mode, string fixedUser, string signedIn, string expected)
    {
        var resolver = new FakeResolver();
        var account = Service(new IdentityOptions { Mode = mode, FixedUser = fixedUser }, resolver).GetAccount(signedIn);
        Assert.Equal(expected, account);
        Assert.Empty(resolver.Asked);
    }

    /// <summary>
    /// Verifies that get account is null when the identity cannot be determined.
    /// </summary>
    [Theory]
    [InlineData("", "", @"CONTOSO\real")]
    [InlineData("Unconfigured", @"CONTOSO\fixed", @"CONTOSO\real")]
    [InlineData("Windows", @"CONTOSO\fixed", null)]
    [InlineData("Windows", "", "   ")]
    [InlineData("Fixed", "", @"CONTOSO\real")]
    public void GetAccount_IsNull_WhenTheIdentityCannotBeDetermined(string mode, string fixedUser, string? signedIn)
    {
        Assert.Null(Service(new IdentityOptions { Mode = mode, FixedUser = fixedUser }).GetAccount(signedIn));
    }
}
