using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Signs a user in by proof from the CRM: issues a one-time challenge, then checks who created the matching CRM record and issues a session token.
/// </summary>
public sealed class CrmLoginService
{
    public const string SubjectPrefix = "NLQ-login-";
    private static readonly Regex NonceShape = new Regex("^[A-Za-z0-9_-]{43}$", RegexOptions.Compiled);
    private static readonly Regex SecretShape = new Regex("^[A-Za-z0-9_-]{43,128}$", RegexOptions.Compiled);

    private readonly IAuthChallengeStore _store;
    private readonly ICrmProofReader _proof;
    private readonly ICrmUserDirectory _directory;
    private readonly AuthTokenService _tokens;
    private readonly AuthOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    public CrmLoginService(IAuthChallengeStore store, ICrmProofReader proof, ICrmUserDirectory directory, AuthTokenService tokens, AuthOptions options, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _proof = proof;
        _directory = directory;
        _tokens = tokens;
        _options = options;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a one-time challenge bound to a secret only the requesting browser knows; only hashes of the nonce and the secret are stored, so someone who sees the nonce in the CRM cannot use it.
    /// </summary>
    public async Task<LoginChallenge?> CreateChallengeAsync(string? secret, CancellationToken ct = default)
    {
        if (secret is null || !SecretShape.IsMatch(secret))
        {
            return null;
        }
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var seconds = Math.Clamp(_options.ChallengeSeconds, 30, 600);
        await _store.AddAsync(Hash(nonce), Hash(secret), _clock().AddSeconds(seconds), ct);
        return new LoginChallenge(nonce, SubjectPrefix + nonce, seconds);
    }

    /// <summary>
    /// Verifies the secret and the proof record for the nonce and returns a session token, or the reason the sign-in was refused.
    /// </summary>
    public async Task<LoginResult> VerifyAsync(string? nonce, string? secret, CancellationToken ct = default)
    {
        if (nonce is null || !NonceShape.IsMatch(nonce) || secret is null || !SecretShape.IsMatch(secret))
        {
            return LoginResult.Refused(LoginError.InvalidChallenge);
        }
        if (!await _store.TryConsumeAsync(Hash(nonce), Hash(secret), _clock(), ct))
        {
            return LoginResult.Refused(LoginError.InvalidChallenge);
        }
        try
        {
            var proof = await _proof.ReadAsync(nonce, ct);
            if (proof is null || proof.CreatedByUserId is null)
            {
                return LoginResult.Refused(LoginError.NoProof);
            }
            if (proof.CreatedOnBehalf)
            {
                return LoginResult.Refused(LoginError.Impersonated);
            }
            if (proof.ModifiedAfterCreation)
            {
                return LoginResult.Refused(LoginError.RecordModified);
            }
            var user = await _directory.GetAsync(proof.CreatedByUserId.Value, ct);
            if (user is null || user.Disabled || string.IsNullOrWhiteSpace(user.Account))
            {
                return LoginResult.Refused(LoginError.NotActiveUser);
            }
            var issued = _tokens.Issue(user.Account, user.UserId, user.Groups, TimeSpan.FromHours(Math.Clamp(_options.TokenHours, 1, 72)));
            return new LoginResult(issued.Token, user.Account, issued.ExpiresAt, LoginError.None);
        }
        catch (Exception ex) when (ex is CrmException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return LoginResult.Refused(LoginError.CrmUnavailable);
        }
    }

    private static string Hash(string nonce) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(nonce)));
}
