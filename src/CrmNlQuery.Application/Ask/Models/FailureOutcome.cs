namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Result type returned by the ask pipeline for failure outcomes.
/// </summary>
public sealed record FailureOutcome(string Message, string Kind = "other") : AskOutcome
{
    public string? Key { get; init; }
    public IReadOnlyDictionary<string, string>? Args { get; init; }

    /// <summary>
    /// Creates a value from key for failure outcome.
    /// </summary>
    public static FailureOutcome FromKey(string key, string kind = "other", IReadOnlyDictionary<string, string>? args = null)
        => new FailureOutcome(CrmNlQuery.Application.Localization.Services.MessageCatalog.Render(key, CrmNlQuery.Domain.Common.Constants.UiLanguage.English, args), kind) { Key = key, Args = args };
}
