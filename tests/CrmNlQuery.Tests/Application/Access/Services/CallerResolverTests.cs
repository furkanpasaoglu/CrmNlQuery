using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests resolving a Windows account to a CRM user.
/// </summary>
public class CallerResolverTests
{
    private static (CrmCallerResolver Resolver, FakeHandler Handler) Create(string responseJson)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(responseJson));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        return (new CrmCallerResolver(client), handler);
    }

    /// <summary>
    /// Verifies that one active user returns its ID.
    /// </summary>
    [Fact]
    public async Task OneActiveUser_ReturnsItsId()
    {
        var id = Guid.NewGuid();
        var (resolver, _) = Create("{\"value\":[{\"systemuserid\":\"" + id + "\"}]}");
        Assert.Equal(id, await resolver.ResolveAsync(@"CONTOSO\testuser_a"));
    }

    /// <summary>
    /// Verifies that no user returns null.
    /// </summary>
    [Fact]
    public async Task NoUser_ReturnsNull()
    {
        var (resolver, _) = Create("""{"value":[]}""");
        Assert.Null(await resolver.ResolveAsync(@"CONTOSO\nobody"));
    }

    /// <summary>
    /// Verifies that more than one user returns null rather than guessing.
    /// </summary>
    [Fact]
    public async Task MoreThanOneUser_ReturnsNull_RatherThanGuessing()
    {
        var json = "{\"value\":[{\"systemuserid\":\"" + Guid.NewGuid() + "\"},{\"systemuserid\":\"" + Guid.NewGuid() + "\"}]}";
        var (resolver, _) = Create(json);
        Assert.Null(await resolver.ResolveAsync(@"CONTOSO\dup"));
    }

    /// <summary>
    /// Verifies that a blank account returns null without calling the CRM.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAccount_ReturnsNull_WithoutCallingCrm(string account)
    {
        var (resolver, handler) = Create("""{"value":[]}""");
        Assert.Null(await resolver.ResolveAsync(account));
        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that the query filters by domain name and excludes disabled users.
    /// </summary>
    [Fact]
    public async Task Query_FiltersByDomainName_AndExcludesDisabledUsers()
    {
        var (resolver, handler) = Create("""{"value":[]}""");
        await resolver.ResolveAsync(@"CONTOSO\testuser_a");
        var url = Uri.UnescapeDataString(handler.Urls[0]);
        Assert.Contains("systemusers?", url);
        Assert.Contains(@"domainname eq 'CONTOSO\testuser_a'", url);
        Assert.Contains("isdisabled eq false", url);
    }

    /// <summary>
    /// Verifies that an apostrophe in the account is escaped for OData.
    /// </summary>
    [Fact]
    public async Task ApostropheInTheAccount_IsEscapedForOData()
    {
        var (resolver, handler) = Create("""{"value":[]}""");
        await resolver.ResolveAsync(@"CONTOSO\o'brien");
        Assert.Contains(@"domainname eq 'CONTOSO\o''brien'", Uri.UnescapeDataString(handler.Urls[0]));
    }

    private sealed class CountingResolver : ICallerResolver
    {
        public int Calls { get; private set; }
        public Guid? Answer { get; set; }
        /// <summary>
        /// Resolver wrapper that counts how many lookups reach the CRM.
        /// </summary>
        public Task<Guid?> ResolveAsync(string windowsAccount, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }

    /// <summary>
    /// Verifies that the cache answers repeated lookups without asking again, ignoring case.
    /// </summary>
    [Fact]
    public async Task Cache_AnswersRepeatedLookupsWithoutAskingAgain_IgnoringCase()
    {
        var inner = new CountingResolver { Answer = Guid.NewGuid() };
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new CachingCallerResolver(inner, () => now, TimeSpan.FromMinutes(10));

        var first = await cache.ResolveAsync(@"CONTOSO\testuser_a");
        var second = await cache.ResolveAsync(@"contoso\TESTUSER_A");

        Assert.Equal(inner.Answer, first);
        Assert.Equal(first, second);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// Verifies that the cache asks again after its time to live.
    /// </summary>
    [Fact]
    public async Task Cache_AsksAgainAfterTheTimeToLive()
    {
        var inner = new CountingResolver { Answer = Guid.NewGuid() };
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new CachingCallerResolver(inner, () => now, TimeSpan.FromMinutes(10));

        await cache.ResolveAsync(@"CONTOSO\a");
        now = now.AddMinutes(11);
        await cache.ResolveAsync(@"CONTOSO\a");

        Assert.Equal(2, inner.Calls);
    }

    /// <summary>
    /// Verifies that the cache does not remember unknown users.
    /// </summary>
    [Fact]
    public async Task Cache_DoesNotRememberUnknownUsers()
    {
        var inner = new CountingResolver { Answer = null };
        var cache = new CachingCallerResolver(inner, () => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

        await cache.ResolveAsync(@"CONTOSO\new");
        inner.Answer = Guid.NewGuid();
        var found = await cache.ResolveAsync(@"CONTOSO\new");

        Assert.Equal(inner.Answer, found);
        Assert.Equal(2, inner.Calls);
    }
}
