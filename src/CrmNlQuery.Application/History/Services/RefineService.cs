using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Continues from a result page with a follow-up such as only some of these records, using the stored query.
/// </summary>
public sealed class RefineService
{
    public const int MaxTextLength = 300;

    private readonly ConversationContext _context;
    private readonly ResultService _results;
    private readonly Func<AskRequest, CancellationToken, Task<AskOutcome>> _run;

    public RefineService(ConversationContext context, ResultService results, Func<AskRequest, CancellationToken, Task<AskOutcome>> run)
    {
        _context = context;
        _results = results;
        _run = run;
    }

    /// <summary>
    /// Refines the caller's stored result with a follow-up question, returning null if it is missing or not theirs.
    /// </summary>
    public async Task<AskOutcome?> RefineAsync(string id, Guid caller, string? text,
        IReadOnlyDictionary<string, string>? answers, CancellationToken ct = default)
    {
        var previous = await _context.LoadAsync(caller, id, ignoreAge: true, ct);
        if (previous is null)
        {
            return null;
        }

        var clean = (text ?? "").Trim();
        if (clean.Length == 0)
        {
            return new FailureOutcome(MessageCatalog.Now(Msg.RefineSay));
        }
        if (clean.Length > MaxTextLength)
        {
            return new FailureOutcome(MessageCatalog.Now(Msg.RefineTooLong, MessageCatalog.Args(("max", MaxTextLength.ToString()))));
        }

        var outcome = await _run(new AskRequest(clean, answers ?? new Dictionary<string, string>(), Caller.User(caller), previous.Query, ForceContinue: true), ct);
        if (outcome is AnswerOutcome answer)
        {
            return await _results.PublishAsync(answer, caller, clean, previous.ResultId, previous.ConversationId, ct);
        }
        return outcome;
    }
}
