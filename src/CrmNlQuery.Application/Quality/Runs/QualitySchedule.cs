namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Holds the scheduled quality run settings: enabled flag, server-local hour and repeat count.
/// </summary>
public sealed record QualitySchedule(bool Enabled, int Hour, int Repeat, string RunAs);
