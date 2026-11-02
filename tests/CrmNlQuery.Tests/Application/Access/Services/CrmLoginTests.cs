using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests the CRM-proof sign-in: token signing, one-time challenges and the rules about who created the proof record.
/// </summary>
public class CrmLoginTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-test-key";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string Secret = new string('s', 64);

    private sealed class Clock
    {
        public DateTimeOffset Now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeStore : IAuthChallengeStore
    {
        private readonly Dictionary<string, (DateTimeOffset Expires, bool Used, string SecretHash)> _items = new Dictionary<string, (DateTimeOffset, bool, string)>();

        public int Count => _items.Count;

        public bool StoresNonceItself(string nonce) => _items.ContainsKey(nonce);

        public Task AddAsync(string nonceHash, string secretHash, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            _items[nonceHash] = (expiresAt, false, secretHash);
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeAsync(string nonceHash, string secretHash, DateTimeOffset now, CancellationToken ct = default)
        {
            if (!_items.TryGetValue(nonceHash, out var item) || item.Used || item.Expires <= now || item.SecretHash != secretHash)
            {
                return Task.FromResult(false);
            }
            _items[nonceHash] = (item.Expires, true, item.SecretHash);
            return Task.FromResult(true);
        }

        public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeProof : ICrmProofReader
    {
        public CrmProof? Result;
        public bool Throw;
        public string? LastNonce;

        public Task<CrmProof?> ReadAsync(string nonce, CancellationToken ct = default)
        {
            LastNonce = nonce;
            if (Throw) { throw new CrmException("down"); }
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeDirectory : ICrmUserDirectory
    {
        public CrmUserRecord? User = new CrmUserRecord(Alice, "CONTOSO\\alice", false, new[] { "Sales Manager" });

        public Task<CrmUserRecord?> GetAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(User is not null && User.UserId == userId ? User : null);
    }

    private sealed class Rig
    {
        public readonly Clock Clock = new Clock();
        public readonly FakeStore Store = new FakeStore();
        public readonly FakeProof Proof = new FakeProof();
        public readonly FakeDirectory Directory = new FakeDirectory();
        public readonly AuthTokenService Tokens;
        public readonly CrmLoginService Login;

        public Rig()
        {
            Tokens = new AuthTokenService(Key, () => Clock.Now);
            Login = new CrmLoginService(Store, Proof, Directory, Tokens, new AuthOptions { SigningKey = Key, TokenHours = 8, ChallengeSeconds = 120 }, () => Clock.Now);
            Proof.Result = new CrmProof("task-1", Alice, false, false);
        }
    }

    [Fact]
    public void ATokenRoundTrips_AndCarriesTheClaims()
    {
        var rig = new Rig();

        var issued = rig.Tokens.Issue("CONTOSO\\alice", Alice, new[] { "Sales", "Admins" }, TimeSpan.FromHours(8));
        var claims = rig.Tokens.Validate(issued.Token);

        Assert.NotNull(claims);
        Assert.Equal("CONTOSO\\alice", claims!.Account);
        Assert.Equal(Alice, claims.UserId);
        Assert.Equal(new[] { "Sales", "Admins" }, claims.Groups);
    }

    [Fact]
    public void ATamperedExpiredOrForeignTokenIsRejected()
    {
        var rig = new Rig();
        var issued = rig.Tokens.Issue("CONTOSO\\alice", Alice, new string[0], TimeSpan.FromHours(1));
        var parts = issued.Token.Split('.');
        var tamperedBody = parts[0].Substring(0, parts[0].Length - 2) + (parts[0].EndsWith("AA") ? "BB" : "AA") + "." + parts[1];
        var otherKey = new AuthTokenService(Key + "-other", () => rig.Clock.Now);

        Assert.Null(rig.Tokens.Validate(tamperedBody));
        Assert.Null(rig.Tokens.Validate(parts[0] + "." + parts[1].Substring(0, parts[1].Length - 2) + "xx"));
        Assert.Null(otherKey.Validate(issued.Token));
        rig.Clock.Now = rig.Clock.Now.AddHours(2);
        Assert.Null(rig.Tokens.Validate(issued.Token));
        Assert.Null(rig.Tokens.Validate("not.a.token"));
        Assert.Null(rig.Tokens.Validate(""));
        Assert.Null(rig.Tokens.Validate(null));
    }

    [Fact]
    public void AShortSigningKeyIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => new AuthTokenService("too-short"));
    }

    [Fact]
    public async Task AChallengeIsStoredOnlyAsAHash_AndHasTheSubjectPrefix()
    {
        var rig = new Rig();

        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        Assert.Equal(43, challenge!.Nonce.Length);
        Assert.Equal("NLQ-login-" + challenge.Nonce, challenge.Subject);
        Assert.Equal(1, rig.Store.Count);
        Assert.False(rig.Store.StoresNonceItself(challenge.Nonce));
    }

    [Fact]
    public async Task AValidProofSignsTheCreatorIn()
    {
        var rig = new Rig();
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var result = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.True(result.Ok);
        Assert.Equal("CONTOSO\\alice", result.Account);
        Assert.Equal(challenge!.Nonce, rig.Proof.LastNonce);
        var claims = rig.Tokens.Validate(result.Token);
        Assert.Equal(Alice, claims!.UserId);
        Assert.Equal(new[] { "Sales Manager" }, claims.Groups);
    }

    [Fact]
    public async Task AChallengeCanBeUsedOnlyOnce()
    {
        var rig = new Rig();
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var first = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);
        var second = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.True(first.Ok);
        Assert.Equal(LoginError.InvalidChallenge, second.Error);
    }

    [Fact]
    public async Task AnExpiredOrUnknownOrMalformedChallengeIsRefusedWithoutReadingTheCrm()
    {
        var rig = new Rig();
        var challenge = await rig.Login.CreateChallengeAsync(Secret);
        rig.Clock.Now = rig.Clock.Now.AddSeconds(121);

        var expired = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);
        var unknown = await rig.Login.VerifyAsync(new string('a', 43), Secret);
        var malformed = await rig.Login.VerifyAsync("short", Secret);
        var missing = await rig.Login.VerifyAsync(null, Secret);

        Assert.Equal(LoginError.InvalidChallenge, expired.Error);
        Assert.Equal(LoginError.InvalidChallenge, unknown.Error);
        Assert.Equal(LoginError.InvalidChallenge, malformed.Error);
        Assert.Equal(LoginError.InvalidChallenge, missing.Error);
        Assert.Null(rig.Proof.LastNonce);
    }

    [Fact]
    public async Task NoProofRecordMeansNoSignIn()
    {
        var rig = new Rig();
        rig.Proof.Result = null;
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var result = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.Equal(LoginError.NoProof, result.Error);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task ARecordCreatedOnBehalfOfSomeoneIsRefused()
    {
        var rig = new Rig();
        rig.Proof.Result = new CrmProof("task-1", Alice, true, false);
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var result = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.Equal(LoginError.Impersonated, result.Error);
    }

    [Fact]
    public async Task ARecordChangedAfterItWasCreatedIsRefused()
    {
        var rig = new Rig();
        rig.Proof.Result = new CrmProof("task-1", Alice, false, true);
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var result = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.Equal(LoginError.RecordModified, result.Error);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task ADisabledOrUnknownCreatorIsRefused()
    {
        var rig = new Rig();
        rig.Directory.User = new CrmUserRecord(Alice, "CONTOSO\\alice", true, new string[0]);
        var disabled = await rig.Login.VerifyAsync((await rig.Login.CreateChallengeAsync(Secret))!.Nonce, Secret);
        rig.Directory.User = null;
        var unknown = await rig.Login.VerifyAsync((await rig.Login.CreateChallengeAsync(Secret))!.Nonce, Secret);

        Assert.Equal(LoginError.NotActiveUser, disabled.Error);
        Assert.Equal(LoginError.NotActiveUser, unknown.Error);
    }

    [Fact]
    public async Task ACrmFailureIsReportedAsUnavailable_AndTheChallengeStaysUsed()
    {
        var rig = new Rig();
        rig.Proof.Throw = true;
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var result = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);
        var again = await rig.Login.VerifyAsync(challenge!.Nonce, Secret);

        Assert.Equal(LoginError.CrmUnavailable, result.Error);
        Assert.Equal(LoginError.InvalidChallenge, again.Error);
    }

    [Fact]
    public async Task SomeoneWhoOnlySeesTheNonceInTheCrmCannotSignIn()
    {
        var rig = new Rig();
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        var withoutSecret = await rig.Login.VerifyAsync(challenge!.Nonce, null);
        var wrongSecret = await rig.Login.VerifyAsync(challenge.Nonce, new string('x', 64));
        var owner = await rig.Login.VerifyAsync(challenge.Nonce, Secret);

        Assert.Equal(LoginError.InvalidChallenge, withoutSecret.Error);
        Assert.Equal(LoginError.InvalidChallenge, wrongSecret.Error);
        Assert.True(owner.Ok);
    }

    [Fact]
    public async Task AWrongSecretDoesNotUseUpTheChallenge()
    {
        var rig = new Rig();
        var challenge = await rig.Login.CreateChallengeAsync(Secret);

        await rig.Login.VerifyAsync(challenge!.Nonce, new string('x', 64));
        var owner = await rig.Login.VerifyAsync(challenge.Nonce, Secret);

        Assert.True(owner.Ok);
    }

    [Fact]
    public async Task AMissingOrMalformedSecretCreatesNoChallenge()
    {
        var rig = new Rig();

        Assert.Null(await rig.Login.CreateChallengeAsync(null));
        Assert.Null(await rig.Login.CreateChallengeAsync("short"));
        Assert.Equal(0, rig.Store.Count);
    }

    [Fact]
    public void AHandoffCodeIsNeverAcceptedAsASession_AndASessionIsNeverAcceptedAsACode()
    {
        var rig = new Rig();
        var issued = rig.Tokens.Issue("CONTOSO\\alice", Alice, new[] { "Sales" }, TimeSpan.FromHours(8));
        var session = rig.Tokens.Validate(issued.Token)!;
        var code = rig.Tokens.IssueHandoff(session, TimeSpan.FromSeconds(30));

        Assert.Null(rig.Tokens.Validate(code));
        Assert.Null(rig.Tokens.ValidateHandoff(issued.Token));
        Assert.NotNull(rig.Tokens.ValidateHandoff(code));
    }

    [Fact]
    public void ARedeemedHandoffGivesASessionThatEndsWhenTheOriginalEnds()
    {
        var rig = new Rig();
        var issued = rig.Tokens.Issue("CONTOSO\\alice", Alice, new[] { "Sales" }, TimeSpan.FromHours(8));
        var session = rig.Tokens.Validate(issued.Token)!;
        var code = rig.Tokens.IssueHandoff(session, TimeSpan.FromSeconds(30));

        var claims = rig.Tokens.ValidateHandoff(code)!;
        var newSession = rig.Tokens.Validate(rig.Tokens.IssueSession(claims))!;

        Assert.Equal(session.ExpiresAt, newSession.ExpiresAt);
        Assert.Equal(session.Account, newSession.Account);
        Assert.Equal(new[] { "Sales" }, newSession.Groups);
    }

    [Fact]
    public void AHandoffCodeExpiresAfterItsShortLife_AndNeverOutlivesTheSession()
    {
        var rig = new Rig();
        var issued = rig.Tokens.Issue("CONTOSO\\alice", Alice, new string[0], TimeSpan.FromSeconds(20));
        var session = rig.Tokens.Validate(issued.Token)!;
        var code = rig.Tokens.IssueHandoff(session, TimeSpan.FromSeconds(30));
        var longerSession = rig.Tokens.Validate(rig.Tokens.Issue("CONTOSO\\alice", Alice, new string[0], TimeSpan.FromHours(1)).Token)!;
        var shortCode = rig.Tokens.IssueHandoff(longerSession, TimeSpan.FromSeconds(30));

        rig.Clock.Now = rig.Clock.Now.AddSeconds(21);
        Assert.Null(rig.Tokens.ValidateHandoff(code));
        rig.Clock.Now = rig.Clock.Now.AddSeconds(15);
        Assert.Null(rig.Tokens.ValidateHandoff(shortCode));
    }
}
