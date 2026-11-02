using CrmNlQuery.Domain.Quality.Constants;

namespace CrmNlQuery.Domain.Quality.Entities;

/// <summary>
/// Represents the result of one case in a run, storing verdict, detail, time and reference data but no rows.
/// </summary>
public sealed record QualityResult
{
    public string RunId { get; init; } = "";
    public string CaseId { get; init; } = "";
    public string Question { get; init; } = "";
    public string Entity { get; init; } = "";
    public bool Verified { get; init; }
    public string Verdict { get; init; } = QualityVerdict.Error;
    public int Passes { get; init; }
    public int Runs { get; init; }
    public string Detail { get; init; } = "";
    public int DurationMs { get; init; }
    public int? ReferenceRows { get; init; }
    public string? Fingerprint { get; init; }
    public bool DataChanged { get; init; }
}
