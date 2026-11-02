namespace CrmNlQuery.Application.Access.Abstractions;

/// <summary>
/// Keeps the one-time sign-in challenges; only hashes of the nonce and of the browser secret are stored, and each challenge can be used once before it expires.
/// </summary>
public interface IAuthChallengeStore
{
    /// <summary>
    /// Stores a new challenge by the hash of its nonce together with the hash of the browser secret that must accompany the verification.
    /// </summary>
    Task AddAsync(string nonceHash, string secretHash, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// Marks the challenge used and returns true only if it exists, is unused, has not expired and the secret hash matches; a wrong secret does not use up the challenge.
    /// </summary>
    Task<bool> TryConsumeAsync(string nonceHash, string secretHash, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Deletes challenges that expired before the given time and returns how many were removed.
    /// </summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct = default);
}
