namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Represents a long-running design assistant job with its status, progress and JSON result.
/// </summary>
public sealed class DesignJob
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Status { get; set; } = DesignJobStatus.Queued;
    public string Step { get; set; } = "";
    public int Percent { get; set; }
    public string Title { get; set; } = "";
    public string? Prefix { get; set; }
    public string Request { get; set; } = "";
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int ProblemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
