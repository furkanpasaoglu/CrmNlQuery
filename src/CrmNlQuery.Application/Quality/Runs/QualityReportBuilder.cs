using CrmNlQuery.Domain.Quality.Constants;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Builds a quality run report from results and the previous run's results.
/// </summary>
public static class QualityReportBuilder
{
    public const int MaxLines = 100;

    /// <summary>
    /// Builds the report with accuracy, regressions, fixes, problem cases and latency from the run's results.
    /// </summary>
    public static QualityReport Build(IReadOnlyList<QualityResult> results, IReadOnlyList<QualityResult>? previous)
    {
        var verified = results.Where(r => r.Verified && QualityVerdict.Counts(r.Verdict)).ToList();
        int Count(string verdict, IEnumerable<QualityResult> from) => from.Count(r => r.Verdict == verdict);
        var passed = Count(QualityVerdict.Pass, verified);
        double? accuracy = verified.Count == 0 ? null : Math.Round(100.0 * passed / verified.Count, 1);

        var previousByCase = (previous ?? Array.Empty<QualityResult>()).ToDictionary(r => r.CaseId, r => r);
        var regressions = new List<QualityResult>();
        var fixedNow = new List<QualityResult>();
        foreach (var r in results.Where(r => r.Verified))
        {
            if (!previousByCase.TryGetValue(r.CaseId, out var before) || !before.Verified) { continue; }
            var wasGood = before.Verdict == QualityVerdict.Pass;
            var isGood = r.Verdict == QualityVerdict.Pass;
            var isBad = QualityVerdict.Counts(r.Verdict) && !isGood;
            if (wasGood && isBad) { regressions.Add(r); }
            else if (QualityVerdict.Counts(before.Verdict) && !wasGood && isGood) { fixedNow.Add(r); }
        }

        var problems = results.Where(r => r.Verdict is QualityVerdict.Fail or QualityVerdict.Flaky or QualityVerdict.Error).ToList();
        var durations = results.Where(r => QualityVerdict.Counts(r.Verdict)).Select(r => r.DurationMs).OrderBy(x => x).ToList();
        var latency = durations.Count == 0
            ? new QualityLatency(0, 0, 0)
            : new QualityLatency((int)Math.Round(durations.Average()), durations[Math.Min(durations.Count - 1, (int)Math.Ceiling(durations.Count * 0.95) - 1)], durations[^1]);

        return new QualityReport(
            results.Count, results.Count(r => r.Verified), results.Count(r => !r.Verified),
            passed, Count(QualityVerdict.Fail, verified), Count(QualityVerdict.Flaky, verified), Count(QualityVerdict.Error, verified),
            Count(QualityVerdict.Skipped, results), Count(QualityVerdict.Unreadable, results),
            accuracy, results.Count(r => r.DataChanged), previous is not null,
            regressions.Take(MaxLines).Select(Line).ToList(), fixedNow.Take(MaxLines).Select(Line).ToList(), problems.Take(MaxLines).Select(Line).ToList(), latency);
    }

    private static QualityCaseLine Line(QualityResult r)
        => new QualityCaseLine(r.CaseId, r.Question, r.Entity, r.Verdict, r.Detail, r.Passes, r.Runs, r.DurationMs, r.DataChanged);
}
