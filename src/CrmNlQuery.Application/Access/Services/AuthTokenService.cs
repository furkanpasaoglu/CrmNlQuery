using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Issues and validates signed, short-lived tokens (HMAC-SHA256) for the CRM-proof sign-in: session tokens and short hand-over codes used to open another page of the service.
/// </summary>
public sealed class AuthTokenService
{
    public const int MinKeyBytes = 32;
    private const int Version = 1;
    private const string SessionPurpose = "s";
    private const string HandoffPurpose = "h";

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;

    public AuthTokenService(string signingKey, Func<DateTimeOffset>? clock = null)
    {
        var key = Encoding.UTF8.GetBytes(signingKey ?? "");
        if (key.Length < MinKeyBytes)
        {
            throw new InvalidOperationException($"Auth:SigningKey must be at least {MinKeyBytes} characters long.");
        }
        _key = key;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private sealed record Payload(int V, string A, Guid U, string[] G, long E, string P, long S);

    /// <summary>
    /// Creates a signed session token that is valid for the given lifetime.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) Issue(string account, Guid userId, IReadOnlyList<string> groups, TimeSpan lifetime)
    {
        var expires = _clock().Add(lifetime);
        return (Sign(new Payload(Version, account, userId, groups.ToArray(), expires.ToUnixTimeSeconds(), SessionPurpose, expires.ToUnixTimeSeconds())), expires);
    }

    /// <summary>
    /// Creates a session token for existing claims, valid until the same moment as the session they came from.
    /// </summary>
    public string IssueSession(AuthTokenClaims claims)
    {
        return Sign(new Payload(Version, claims.Account, claims.UserId, claims.Groups.ToArray(), claims.ExpiresAt.ToUnixTimeSeconds(), SessionPurpose, claims.ExpiresAt.ToUnixTimeSeconds()));
    }

    /// <summary>
    /// Creates a short-lived hand-over code for a signed-in session; it can only be exchanged for a session that ends when the original one ends and is never accepted as a session itself.
    /// </summary>
    public string IssueHandoff(AuthTokenClaims session, TimeSpan life)
    {
        var expires = _clock().Add(life);
        if (expires > session.ExpiresAt)
        {
            expires = session.ExpiresAt;
        }
        return Sign(new Payload(Version, session.Account, session.UserId, session.Groups.ToArray(), expires.ToUnixTimeSeconds(), HandoffPurpose, session.ExpiresAt.ToUnixTimeSeconds()));
    }

    /// <summary>
    /// Returns the claims if the token is a valid, unexpired session token; hand-over codes and any other token are refused.
    /// </summary>
    public AuthTokenClaims? Validate(string? token)
    {
        var payload = Read(token);
        if (payload is null || payload.P != SessionPurpose)
        {
            return null;
        }
        return new AuthTokenClaims(payload.A, payload.U, payload.G ?? new string[0], DateTimeOffset.FromUnixTimeSeconds(payload.E));
    }

    /// <summary>
    /// Returns the claims of the session a valid, unexpired hand-over code was made from (expiring when that session ends), or null.
    /// </summary>
    public AuthTokenClaims? ValidateHandoff(string? code)
    {
        var payload = Read(code);
        if (payload is null || payload.P != HandoffPurpose)
        {
            return null;
        }
        var sessionEnds = DateTimeOffset.FromUnixTimeSeconds(payload.S);
        if (sessionEnds <= _clock())
        {
            return null;
        }
        return new AuthTokenClaims(payload.A, payload.U, payload.G ?? new string[0], sessionEnds);
    }

    private Payload? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192)
        {
            return null;
        }
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }
        try
        {
            var expected = Hash(parts[0]);
            var given = FromBase64Url(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, given))
            {
                return null;
            }
            var payload = JsonSerializer.Deserialize<Payload>(FromBase64Url(parts[0]));
            if (payload is null || payload.V != Version || string.IsNullOrWhiteSpace(payload.A) || payload.U == Guid.Empty)
            {
                return null;
            }
            return DateTimeOffset.FromUnixTimeSeconds(payload.E) <= _clock() ? null : payload;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private string Sign(Payload payload)
    {
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        return body + "." + Base64Url(Hash(body));
    }

    private byte[] Hash(string body) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
