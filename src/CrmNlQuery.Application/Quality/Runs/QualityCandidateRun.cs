using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Result of running one quality case's question through the live pipeline.
/// </summary>
public sealed record QualityCandidateRun(string Kind, ResultTable? Table, string Message, int DurationMs);
