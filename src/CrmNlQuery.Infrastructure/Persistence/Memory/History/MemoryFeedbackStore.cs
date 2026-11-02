using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Domain.Feedback.Constants;
using CrmNlQuery.Domain.Feedback.Entities;
using CrmNlQuery.Application.Feedback.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory.History;

/// <summary>
/// Keeps feedback votes and candidates in memory when no database is configured and in tests.
/// </summary>
public sealed class MemoryFeedbackStore : IFeedbackStore
{
    private readonly Func<string, bool> _historyExists;
    private readonly object _gate = new object();
    private readonly Dictionary<string, FeedbackVote> _votes = new Dictionary<string, FeedbackVote>(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid Owner, FeedbackCandidate Candidate)> _candidates = new Dictionary<string, (Guid, FeedbackCandidate)>(StringComparer.Ordinal);

    public MemoryFeedbackStore(Func<string, bool> historyExists)
    {
        _historyExists = historyExists;
    }

    /// <summary>
    /// Inserts or updates a vote and optionally its shared candidate in memory.
    /// </summary>
    public Task<bool> UpsertAsync(FeedbackVote vote, FeedbackCandidate? candidate, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_votes.TryGetValue(vote.ResultId, out var existing))
            {
                if (existing.Owner != vote.Owner)
                {
                    return Task.FromResult(false);
                }
                vote = vote with { CreatedAt = existing.CreatedAt };
            }
            _votes[vote.ResultId] = vote;
            if (candidate is null)
            {
                _candidates.Remove(vote.ResultId);
            }
            else
            {
                if (_candidates.TryGetValue(vote.ResultId, out var old))
                {
                    candidate = candidate with { CreatedAt = old.Candidate.CreatedAt };
                }
                _candidates[vote.ResultId] = (vote.Owner, candidate);
            }
        }
        return Task.FromResult(true);
    }

    /// <summary>
    /// Removes the owner vote and its candidate for a result.
    /// </summary>
    public Task<bool> RemoveAsync(Guid owner, string resultId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_votes.TryGetValue(resultId, out var vote) || vote.Owner != owner || !_historyExists(resultId))
            {
                return Task.FromResult(false);
            }
            _votes.Remove(resultId);
            _candidates.Remove(resultId);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Returns the owner vote states for the given result ids.
    /// </summary>
    public Task<IReadOnlyDictionary<string, FeedbackState>> GetStatesAsync(Guid owner, IReadOnlyList<string> resultIds, CancellationToken ct = default)
    {
        var map = new Dictionary<string, FeedbackState>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var id in resultIds)
            {
                if (_votes.TryGetValue(id, out var vote) && vote.Owner == owner && _historyExists(id))
                {
                    map[id] = new FeedbackState(vote.Rating, vote.Reason, vote.Shared && LiveCandidate(id));
                }
            }
        }
        return Task.FromResult<IReadOnlyDictionary<string, FeedbackState>>(map);
    }

    /// <summary>
    /// Summarizes votes for a date range.
    /// </summary>
    public Task<FeedbackSummary> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var votes = _votes.Values.Where(v => v.UpdatedAt >= from && v.UpdatedAt < to).ToList();
            var down = votes.Where(v => v.Rating == FeedbackRatings.Down).ToList();
            var summary = new FeedbackSummary(
                votes.Count(v => v.Rating == FeedbackRatings.Up),
                down.Count,
                down.Where(v => v.Reason is not null).GroupBy(v => v.Reason!).Select(g => new FeedbackCount(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Key, StringComparer.Ordinal).ToList(),
                down.SelectMany(v => v.Tables).GroupBy(t => t).Select(g => new FeedbackCount(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Key, StringComparer.Ordinal).Take(10).ToList(),
                down.Count(v => v.Steps is null or <= 1),
                down.Count(v => v.Steps > 1),
                votes.GroupBy(v => DateOnly.FromDateTime(v.UpdatedAt.UtcDateTime)).OrderBy(g => g.Key)
                    .Select(g => new FeedbackDay(g.Key, g.Count(v => v.Rating == FeedbackRatings.Up), g.Count(v => v.Rating == FeedbackRatings.Down))).ToList());
            return Task.FromResult(summary);
        }
    }

    /// <summary>
    /// Returns the in-memory shared golden-set candidates newest first, skipping those whose history entry no longer exists, up to the requested number.
    /// </summary>
    public Task<IReadOnlyList<FeedbackCandidate>> ListCandidatesAsync(int take, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var list = _candidates.Values.Select(c => c.Candidate).Where(c => _historyExists(c.ResultId))
                .OrderByDescending(c => c.CreatedAt).Take(Math.Max(1, take)).ToList();
            return Task.FromResult<IReadOnlyList<FeedbackCandidate>>(list);
        }
    }

    /// <summary>
    /// Deletes the shared candidate of a result.
    /// </summary>
    public Task<bool> DeleteCandidateAsync(string resultId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_candidates.Remove(resultId))
            {
                return Task.FromResult(false);
            }
            if (_votes.TryGetValue(resultId, out var vote))
            {
                _votes[resultId] = vote with { Shared = false };
            }
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Deletes old votes and candidates by their cutoff dates.
    /// </summary>
    public Task<int> PurgeAsync(DateTimeOffset? voteCutoff, DateTimeOffset? candidateCutoff, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var removed = 0;
            if (candidateCutoff is { } cc)
            {
                foreach (var id in _candidates.Where(c => c.Value.Candidate.CreatedAt < cc).Select(c => c.Key).ToList())
                {
                    _candidates.Remove(id);
                    removed++;
                }
            }
            if (voteCutoff is { } vc)
            {
                foreach (var id in _votes.Where(v => v.Value.UpdatedAt < vc).Select(v => v.Key).ToList())
                {
                    _votes.Remove(id);
                    removed++;
                }
            }
            return Task.FromResult(removed);
        }
    }

    /// <summary>
    /// Returns the owner of a vote row, or null once the linked history entry is deleted and the user id removed.
    /// </summary>
    public Guid? OwnerOf(string resultId)
    {
        lock (_gate)
        {
            return _votes.TryGetValue(resultId, out var vote) && _historyExists(resultId) ? vote.Owner : null;
        }
    }

    private bool LiveCandidate(string id) => _candidates.ContainsKey(id) && _historyExists(id);
}
