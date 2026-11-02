using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Provides the fixed user-facing error messages in the language of the request, with texts held in the message catalog.
/// </summary>
public static class UserMessages
{
    public static string CrmUnavailable => MessageCatalog.Now(Msg.CrmUnavailable);
    public static string ModelUnavailable => MessageCatalog.Now(Msg.ModelUnavailable);
    public static string ModelOutput => MessageCatalog.Now(Msg.ModelOutput);
    public static string ContextOverflow => MessageCatalog.Now(Msg.ContextOverflow);
    public static string BudgetExceeded => MessageCatalog.Now(Msg.BudgetExceeded);

    /// <summary>
    /// Returns the fixed message for a request that timed out after the given seconds.
    /// </summary>
    public static string TimedOut(int seconds) => MessageCatalog.Now(Msg.TimedOut, MessageCatalog.Args(("seconds", seconds.ToString())));

    /// <summary>
    /// Returns the message saying a chain step passed too many ids (limit 100) and asking the user to narrow the question.
    /// </summary>
    public static string TooManyIds(int step, int count) =>
        MessageCatalog.Now(Msg.TooManyIds, MessageCatalog.Args(("step", step.ToString()), ("count", count.ToString()), ("max", Steps.StepInputFiller.MaxIds.ToString())));

    public static string AggregateTooBig => MessageCatalog.Now(Msg.AggregateTooBig);
    public static string NeedsSteps => MessageCatalog.Now(Msg.NeedsSteps);
    public static string CannotAnswer => MessageCatalog.Now(Msg.CannotAnswer);
    public static string UnknownRecord => MessageCatalog.Now(Msg.UnknownRecord);
    public static string DraftNeedsRecord => MessageCatalog.Now(Msg.DraftNeedsRecord);
    public static string DraftFailed => MessageCatalog.Now(Msg.DraftFailed);
    public static string ChainTooComplex => MessageCatalog.Now(Msg.ChainTooComplex);

    private static readonly string[] CommonEntities = { "account", "contact", "opportunity", "lead", "incident", "product", "quote", "salesorder", "invoice", "contract" };

    /// <summary>
    /// Returns the question asking which kind of record the user means, listing the given entities.
    /// </summary>
    public static string WhichKindOfRecord(IReadOnlyList<CrmNlQuery.Domain.Metadata.Entities.EntitySummary> entities)
    {
        var labels = CommonEntities
            .Select(name => entities.FirstOrDefault(e => string.Equals(e.LogicalName, name, StringComparison.OrdinalIgnoreCase)))
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Label))
            .Select(e => e!.Label)
            .Take(3)
            .ToList();
        var example = labels.Count == 0 ? "" : MessageCatalog.Now(Msg.WhichKindExample, MessageCatalog.Args(("labels", string.Join(", ", labels))));
        return MessageCatalog.Now(Msg.WhichKind, MessageCatalog.Args(("example", example)));
    }

    /// <summary>
    /// Appends the admin-configured example questions to a not-understood message, if any are configured.
    /// </summary>
    public static string WithExamples(string message, IReadOnlyList<string>? examples)
    {
        if (examples is null || examples.Count == 0)
        {
            return message;
        }
        var shown = examples.Take(2).Select(x => "\"" + x + "\"");
        return message + MessageCatalog.Now(Msg.ExamplesSuffix, MessageCatalog.Args(("examples", string.Join(MessageCatalog.Now(Msg.Or), shown))));
    }

    /// <summary>
    /// Returns the message saying the question exceeds the length limit.
    /// </summary>
    public static string QuestionTooLong(int limit) => MessageCatalog.Now(Msg.QuestionTooLong, MessageCatalog.Args(("limit", limit.ToString())));

    /// <summary>
    /// Maps a CRM exception to a fixed user message, keeping permission and service-account setup messages and hiding other detail.
    /// </summary>
    public static string ForCrm(CrmException ex)
    {
        if (ex.IsAggregateLimit)
        {
            return AggregateTooBig;
        }
        return ex.IsPermissionDenied || ex.IsImpersonationDenied ? ex.Message : CrmUnavailable;
    }
}
