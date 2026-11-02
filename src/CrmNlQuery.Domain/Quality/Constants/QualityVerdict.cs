namespace CrmNlQuery.Domain.Quality.Constants;

/// <summary>
/// Defines the verdict values of a case in a run; skipped and unreadable cases do not count toward accuracy.
/// </summary>
public static class QualityVerdict
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Flaky = "flaky";
    public const string Error = "error";
    public const string Skipped = "skipped";
    public const string Unreadable = "unreadable";

    /// <summary>
    /// Tells whether a verdict counts toward the accuracy rate.
    /// </summary>
    public static bool Counts(string verdict) => verdict is Pass or Fail or Flaky or Error;
}
