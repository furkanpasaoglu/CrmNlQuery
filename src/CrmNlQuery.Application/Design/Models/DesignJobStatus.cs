namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Holds the string constants for the states of a design job.
/// </summary>
public static class DesignJobStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Tells whether a design job status means the job is still queued or running.
    /// </summary>
    public static bool IsActive(string status) => status is Queued or Running;
}
