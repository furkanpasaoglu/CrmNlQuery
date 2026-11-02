namespace CrmNlQuery.Domain.Feedback.Constants;

/// <summary>
/// Defines the fixed list of feedback reasons, with no free text because of personal data risk.
/// </summary>
public static class FeedbackReasons
{
    public const string WrongRecords = "wrong_records";
    public const string WrongColumns = "wrong_columns";
    public const string Misunderstood = "misunderstood";
    public const string TooSlow = "too_slow";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new[] { WrongRecords, WrongColumns, Misunderstood, TooSlow, Other };

    /// <summary>
    /// Checks whether a feedback reason is one of the allowed values.
    /// </summary>
    public static bool IsValid(string? reason) => reason is not null && All.Contains(reason);
}
