using System.Xml.Linq;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Feedback.Abstractions;
using CrmNlQuery.Application.Feedback.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Domain.Feedback.Constants;
using CrmNlQuery.Domain.Feedback.Entities;

namespace CrmNlQuery.Application.Feedback.Services;

/// <summary>
/// Handles thumbs up/down with a reason; sharing happens only on thumbs down, at the user's explicit request and when sharing is allowed.
/// </summary>
public sealed class FeedbackService
{
    public const int MaxCandidates = 200;

    private readonly IFeedbackStore _store;
    private readonly IHistoryStore _history;
    private readonly QueryExplainer _explainer;
    private readonly AccessService _access;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _allowSharing;
    private readonly Func<DateTimeOffset> _clock;

    public FeedbackService(IFeedbackStore store, IHistoryStore history, QueryExplainer explainer, AccessService access,
        Func<bool> enabled, Func<bool> allowSharing, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _history = history;
        _explainer = explainer;
        _access = access;
        _enabled = enabled;
        _allowSharing = allowSharing;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool Enabled => _enabled();

    public bool AllowSharing => _enabled() && _allowSharing();

    /// <summary>
    /// Records a rating with an optional reason and sharing choice for the owner's result.
    /// </summary>
    public async Task<FeedbackResult> RateAsync(Guid owner, string resultId, string? rating, string? reason, bool share, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            return new FeedbackResult(FeedbackStatus.NotFound);
        }
        if (!FeedbackRatings.IsValid(rating))
        {
            return new FeedbackResult(FeedbackStatus.Invalid, "Rating must be up or down.");
        }
        var down = rating == FeedbackRatings.Down;
        if (reason is not null && (!down || !FeedbackReasons.IsValid(reason)))
        {
            return new FeedbackResult(FeedbackStatus.Invalid, "That reason is not allowed.");
        }
        if (share && (!down || !_allowSharing()))
        {
            return new FeedbackResult(FeedbackStatus.Invalid, "Sharing is not possible here.");
        }
        var entry = await _history.GetAsync(owner, resultId, ct);
        if (entry is null || entry.Kind is not null)
        {
            return new FeedbackResult(FeedbackStatus.NotFound);
        }

        var now = _clock();
        var vote = new FeedbackVote(entry.Id, owner, rating!, reason, share, TablesOf(entry.FetchXml), entry.Steps, entry.DurationMs, now, now);
        FeedbackCandidate? candidate = null;
        if (share)
        {
            candidate = new FeedbackCandidate(entry.Id, entry.Question, await ExplanationTextAsync(entry.FetchXml, ct), reason, now);
        }
        if (!await _store.UpsertAsync(vote, candidate, ct))
        {
            return new FeedbackResult(FeedbackStatus.NotFound);
        }
        return new FeedbackResult(FeedbackStatus.Ok, null, new FeedbackState(vote.Rating, vote.Reason, vote.Shared));
    }

    /// <summary>
    /// Removes a vote and any shared candidate, working even when the feature is off so users can always withdraw.
    /// </summary>
    public async Task<FeedbackResult> RemoveAsync(Guid owner, string resultId, CancellationToken ct = default)
    {
        return await _store.RemoveAsync(owner, resultId, ct) ? new FeedbackResult(FeedbackStatus.Ok) : new FeedbackResult(FeedbackStatus.NotFound);
    }

    /// <summary>
    /// Returns the feedback states of the owner's results.
    /// </summary>
    public Task<IReadOnlyDictionary<string, FeedbackState>> StatesAsync(Guid owner, IReadOnlyList<string> resultIds, CancellationToken ct = default)
        => _enabled() ? _store.GetStatesAsync(owner, resultIds, ct) : Task.FromResult<IReadOnlyDictionary<string, FeedbackState>>(new Dictionary<string, FeedbackState>());

    /// <summary>
    /// Returns the feedback summary for the given number of days.
    /// </summary>
    public Task<FeedbackSummary> SummaryAsync(int days, CancellationToken ct = default)
    {
        var now = _clock();
        return _store.SummaryAsync(now.AddDays(-Math.Clamp(days, 1, 365)), now.AddSeconds(1), ct);
    }

    /// <summary>
    /// Lists shared candidates for an admin, writing an audit record for every view and refusing to list if it cannot be written.
    /// </summary>
    public async Task<IReadOnlyList<FeedbackCandidate>> CandidatesAsync(string actorAccount, int take, CancellationToken ct = default)
    {
        var list = await _store.ListCandidatesAsync(Math.Clamp(take, 1, MaxCandidates), ct);
        await _access.RecordViewAsync("feedback.view", actorAccount, "feedback", "", $"{list.Count} candidates", rateLimit: false, ct);
        return list;
    }

    /// <summary>
    /// Deletes a shared candidate on behalf of an admin account.
    /// </summary>
    public async Task<bool> DeleteCandidateAsync(string actorAccount, string resultId, CancellationToken ct = default)
    {
        var removed = await _store.DeleteCandidateAsync(resultId, ct);
        if (removed)
        {
            await _access.RecordAsync(actorAccount, "feedback.delete", "feedback", "1 candidate", ct);
        }
        return removed;
    }

    /// <summary>
    /// Returns the distinct names of the root and linked tables in a FetchXML, without any values.
    /// </summary>
    public static IReadOnlyList<string> TablesOf(string fetchXml)
    {
        try
        {
            var entity = XDocument.Parse(fetchXml).Root?.Element("entity");
            if (entity is null)
            {
                return Array.Empty<string>();
            }
            var names = new List<string>();
            var root = (string?)entity.Attribute("name");
            if (!string.IsNullOrEmpty(root)) { names.Add(root); }
            foreach (var link in entity.Descendants("link-entity"))
            {
                var name = (string?)link.Attribute("name");
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) { names.Add(name); }
            }
            return names;
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<string>();
        }
    }

    private async Task<string> ExplanationTextAsync(string fetchXml, CancellationToken ct)
    {
        QueryExplanation? e;
        try
        {
            e = await _explainer.ExplainAsync(fetchXml, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            e = null;
        }
        if (e is null)
        {
            return "(no explanation available)";
        }
        var parts = new List<string>();
        foreach (var f in e.Filters)
        {
            var values = string.Join(", ", f.Values.Select(v => v.Label ?? v.Value ?? "a specific record"));
            parts.Add($"{(f.Via is null ? "" : f.Via + ": ")}{f.FieldLabel} {f.Op}{(values.Length > 0 ? " " + values : "")}");
        }
        if (e.GroupBy.Count > 0) { parts.Add("grouped by " + string.Join(", ", e.GroupBy)); }
        foreach (var m in e.Measures) { parts.Add(m.Fn + (m.FieldLabel is null ? "" : " of " + m.FieldLabel)); }
        foreach (var s in e.Sort) { parts.Add("sorted by " + s.Label + (s.Descending ? " descending" : " ascending")); }
        if (e.Top is int top) { parts.Add("first " + top); }
        return e.EntityLabel + (parts.Count > 0 ? " · " + string.Join("; ", parts) : "");
    }
}
