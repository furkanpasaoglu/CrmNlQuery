namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Short summary of the last scheduled quality run for the panel card, without result rows.
/// </summary>
public sealed record ScheduledRunSummary(string Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Total, double? AccuracyPercent, int Regressions, int Errors, string? Error);
