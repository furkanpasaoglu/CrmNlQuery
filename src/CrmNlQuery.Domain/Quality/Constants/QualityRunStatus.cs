namespace CrmNlQuery.Domain.Quality.Constants;

/// <summary>
/// Defines the status values of a quality run.
/// </summary>
public static class QualityRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    /// <summary>
    /// Tells whether a quality run status is still active.
    /// </summary>
    public static bool IsActive(string status) => status is Queued or Running;
}
