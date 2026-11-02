using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Suggestions.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Suggestions.Services;

/// <summary>
/// Produces follow-up suggestions for an answer and applies a chosen suggestion without the model.
/// </summary>
public sealed class SuggestionService
{
    private readonly IHistoryStore _history;
    private readonly IMetadataSource _metadata;
    private readonly PlanValidator _validator;
    private readonly ResultService _results;
    private readonly ChatService _chats;
    private readonly ISuggestionStats? _stats;

    public SuggestionService(IHistoryStore history, IMetadataSource metadata, PlanValidator validator, ResultService results, ChatService chats, ISuggestionStats? stats = null)
    {
        _stats = stats;
        _history = history;
        _metadata = metadata;
        _validator = validator;
        _results = results;
        _chats = chats;
    }

    private static FailureOutcome Invalid() => new FailureOutcome(MessageCatalog.Now(Msg.SuggestionInvalid), "suggestion_invalid");

    /// <summary>
    /// Builds suggestions for an answer without breaking it, returning none if the schema cannot be read.
    /// </summary>
    public async Task<IReadOnlyList<Suggestion>> ForAsync(AnswerOutcome answer, bool countShown = false, CancellationToken ct = default)
    {
        if (answer.FromChain)
        {
            return Array.Empty<Suggestion>();
        }
        try
        {
            var entity = await _metadata.GetEntityAsync(answer.Entity, ct);
            if (entity is null)
            {
                return Array.Empty<Suggestion>();
            }
            var entities = await _metadata.ListEntitiesAsync(ct);
            var list = SuggestionBuilder.For(answer.FetchXml, entity, answer.Table, answer.Truncated, entities);
            if (countShown && list.Count > 0)
            {
                await CountAsync(() => _stats!.AddShownAsync(list.Select(s => s.Kind).ToList(), ct));
            }
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<Suggestion>();
        }
    }

    /// <summary>
    /// Counts the click of a suggestion that is sent as a question.
    /// </summary>
    public Task ClickAsync(string kind, CancellationToken ct = default)
    {
        return SuggestionKinds.IsKnown(kind) ? CountAsync(() => _stats!.AddClickAsync(kind, ct)) : Task.CompletedTask;
    }

    private async Task CountAsync(Func<Task> action)
    {
        if (_stats is null)
        {
            return;
        }
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Applies a chosen suggestion to the stored result as the caller and returns the new outcome.
    /// </summary>
    public async Task<AskOutcome?> ApplyAsync(string resultId, Guid caller, string suggestionId, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(caller, resultId, ct);
        if (entry is null || entry.Kind is not null)
        {
            return null;
        }
        var entity = await _metadata.GetEntityAsync(entry.Entity, ct);
        var applied = entity is null ? null : SuggestionTransform.Apply(entry.FetchXml, entity, suggestionId);
        if (applied is null)
        {
            return Invalid();
        }
        var validation = await _validator.ValidateFetchXmlAsync(applied.Document.ToString(SaveOptions.DisableFormatting), ct);
        if (!validation.IsValid || validation.NormalizedFetchXml is null)
        {
            return Invalid();
        }

        using var accessScope = AccessLogContext.Begin("result");
        var answer = await _results.ExecuteStoredAsync(entry.Entity, validation.NormalizedFetchXml, Caller.User(caller), ct);
        if (answer is null)
        {
            return null;
        }
        var original = entry.Question;
        var root = original;
        foreach (var marker in new[] { " (then: ", " (edited: " })
        {
            var at = root.IndexOf(marker, StringComparison.Ordinal);
            if (at > 0)
            {
                root = root.Substring(0, at);
            }
        }
        var question = Shorten(root) + " (then: " + applied.Label + ")";
        var chat = await _chats.EnsureAsync(caller, entry.ConversationId, question, ct);
        var published = await _results.PublishAsync(answer, caller, question, resultId, chat, ct, adminViewable: false);
        await ClickAsync(suggestionId.Split(':')[0], ct);
        return published;
    }

    private static string Shorten(string question)
    {
        var clean = question.Trim();
        return clean.Length <= 200 ? clean : clean.Substring(0, 199) + "…";
    }
}
