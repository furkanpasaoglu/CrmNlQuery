using CrmNlQuery.Domain.Feedback.Entities;
using CrmNlQuery.Application.Feedback.Models;

namespace CrmNlQuery.Application.Feedback.Abstractions;

/// <summary>
/// Stores feedback votes and shared candidates; owner-scoped operations are separate from admin operations that never look up an owner.
/// </summary>
public interface IFeedbackStore
{
    /// <summary>
    /// Writes or updates a vote, writing or updating the shared candidate when given and deleting any existing candidate when null.
    /// </summary>
    Task<bool> UpsertAsync(FeedbackVote vote, FeedbackCandidate? candidate, CancellationToken ct = default);

    /// <summary>
    /// Deletes the vote and candidate for the owner and result, returning true if a vote existed.
    /// </summary>
    Task<bool> RemoveAsync(Guid owner, string resultId, CancellationToken ct = default);

    /// <summary>
    /// Returns the feedback state for the owner for each of the given result ids.
    /// </summary>
    Task<IReadOnlyDictionary<string, FeedbackState>> GetStatesAsync(Guid owner, IReadOnlyList<string> resultIds, CancellationToken ct = default);

    /// <summary>
    /// Returns the feedback summary for a date range.
    /// </summary>
    Task<FeedbackSummary> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Lists shared candidates newest first without returning any user identity.
    /// </summary>
    Task<IReadOnlyList<FeedbackCandidate>> ListCandidatesAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// Deletes the shared candidate for a result.
    /// </summary>
    Task<bool> DeleteCandidateAsync(string resultId, CancellationToken ct = default);

    /// <summary>
    /// Deletes votes and candidates older than their own cutoffs, where a null cutoff skips that kind and vote cleanup never touches candidates.
    /// </summary>
    Task<int> PurgeAsync(DateTimeOffset? voteCutoff, DateTimeOffset? candidateCutoff, CancellationToken ct = default);
}
