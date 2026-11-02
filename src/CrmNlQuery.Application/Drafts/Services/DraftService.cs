using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Drafts.Models;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Drafts.Services;

/// <summary>
/// Builds email drafts: reads values with the caller's permissions, has the model write only a placeholder template, and fills it in code.
/// </summary>
public sealed class DraftService
{
    private readonly DraftValueReader _reader;
    private readonly DraftComposer _composer;
    private readonly Func<Caller, CancellationToken, Task<string?>> _senderName;

    public DraftService(DraftValueReader reader, DraftComposer composer, Func<Caller, CancellationToken, Task<string?>>? senderName = null)
    {
        _reader = reader;
        _composer = composer;
        _senderName = senderName ?? ((_, _) => Task.FromResult<string?>(null));
    }

    private async Task<(DraftValueReader.Data Data, Dictionary<string, string> Values)> ReadValuesAsync(Caller caller, string entity, Guid id, string name, CancellationToken ct)
    {
        var data = await _reader.ReadAsync(caller, entity, id, name, ct);
        var values = new Dictionary<string, string>(data.Values, StringComparer.Ordinal);
        var sender = await _senderName(caller, ct);
        if (!string.IsNullOrWhiteSpace(sender))
        {
            values[DraftPlaceholders.SenderName] = sender.Trim();
        }
        return (data, values);
    }

    /// <summary>
    /// Reopens a stored draft without calling the model, refilling its template with fresh values as the caller.
    /// </summary>
    public async Task<DraftOutcome> RefillAsync(Caller caller, string entity, Guid id, DraftPayload payload, CancellationToken ct = default)
    {
        var name = await _reader.ReadNameAsync(caller, entity, id, ct);
        var (data, values) = await ReadValuesAsync(caller, entity, id, name, ct);
        return Fill(entity, id, name, payload.Subject, payload.Body, payload.Tone, values, data.To);
    }

    private static DraftOutcome Fill(string entity, Guid id, string name, string templateSubject, string templateBody, string tone, IReadOnlyDictionary<string, string> values, string? to)
    {
        var subject = DraftFiller.Fill(templateSubject, values);
        var body = DraftFiller.Fill(templateBody, values);
        var unresolved = subject.Unresolved.Concat(body.Unresolved).Distinct().ToList();
        return new DraftOutcome(entity, id.ToString("N"), name, subject.Text, body.Text, to, tone, unresolved, templateSubject, templateBody);
    }

    /// <summary>
    /// Builds a draft for a target record with caller-read values and a model-written template.
    /// </summary>
    public async Task<AskOutcome> BuildAsync(Caller caller, string entity, Guid id, string name, string question, CancellationToken ct = default)
    {
        var (data, values) = await ReadValuesAsync(caller, entity, id, name, ct);

        var offered = DraftPlaceholders.All.Where(p => p.Name == DraftPlaceholders.SenderName || values.ContainsKey(p.Name)).ToList();
        var tone = DraftIntent.ToneOf(question);
        var template = await _composer.ComposeAsync(question, tone, offered, ct);
        if (template is null)
        {
            return new FailureOutcome(UserMessages.DraftFailed, "draft_invalid");
        }

        return Fill(entity, id, name, template.Subject, template.Body, tone, values, data.To);
    }
}
