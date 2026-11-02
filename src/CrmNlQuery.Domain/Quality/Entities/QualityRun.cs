using CrmNlQuery.Domain.Quality.Constants;

namespace CrmNlQuery.Domain.Quality.Entities;

/// <summary>
/// Represents a quality check run with its status, progress and summary.
/// </summary>
public sealed record QualityRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string StartedBy { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string Status { get; init; } = QualityRunStatus.Queued;
    public int Repeat { get; init; } = 1;
    public int Total { get; init; }
    public int Done { get; init; }
    public string? SummaryJson { get; init; }
    public string? Error { get; init; }
}
