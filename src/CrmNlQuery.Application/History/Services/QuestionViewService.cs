using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Models;
using CrmNlQuery.Domain.History.Entities;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Application service implementing the question view rules.
/// </summary>
public sealed class QuestionViewService
{
    private const int MaxTake = 200;

    private readonly IHistoryStore _history;
    private readonly ICallerResolver _resolver;
    private readonly AccessService _access;
    private readonly Func<bool> _enabled;

    public QuestionViewService(IHistoryStore history, ICallerResolver resolver, AccessService access, Func<bool> enabled)
    {
        _history = history;
        _resolver = resolver;
        _access = access;
        _enabled = enabled;
    }

    public bool Enabled => _enabled();

    /// <summary>
    /// Lists by account for question view service.
    /// </summary>
    public async Task<QuestionViewResult> ListByAccountAsync(string actorAccount, string account, int take, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            return new QuestionViewResult(QuestionViewStatus.NotEnabled, new List<ViewableQuestion>());
        }
        var user = await _resolver.ResolveAsync((account ?? "").Trim(), ct);
        if (user is null)
        {
            return new QuestionViewResult(QuestionViewStatus.NotFound, new List<ViewableQuestion>());
        }
        return await ListByIdAsync(actorAccount, user.Value, take, ct);
    }

    /// <summary>
    /// Lists by id for question view service.
    /// </summary>
    public async Task<QuestionViewResult> ListByIdAsync(string actorAccount, Guid user, int take, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            return new QuestionViewResult(QuestionViewStatus.NotEnabled, new List<ViewableQuestion>());
        }
        var questions = await _history.ListViewableQuestionsAsync(user, Math.Clamp(take, 1, MaxTake), ct);
        await _access.RecordViewAsync("questions.view", actorAccount, user.ToString(), "", $"{questions.Count} questions", rateLimit: false, ct);
        return new QuestionViewResult(QuestionViewStatus.Ok, questions);
    }
}
