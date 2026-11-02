namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Report of one quality run; accuracy counts only verified cases and skipped or unreadable cases are not model errors.
/// </summary>
public sealed record QualityReport(
    int Total, int Verified, int Unverified, int Passed, int Failed, int Flaky, int Errors, int Skipped, int Unreadable,
    double? AccuracyPercent, int DataChanged, bool HasPrevious,
    IReadOnlyList<QualityCaseLine> Regressions, IReadOnlyList<QualityCaseLine> Fixed, IReadOnlyList<QualityCaseLine> Problems, QualityLatency Latency);
