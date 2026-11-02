namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// One case line of a quality run report with verdict, detail, passes and duration.
/// </summary>
public sealed record QualityCaseLine(string CaseId, string Question, string Entity, string Verdict, string Detail, int Passes, int Runs, int DurationMs, bool DataChanged);
