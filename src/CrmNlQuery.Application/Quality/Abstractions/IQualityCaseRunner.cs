using CrmNlQuery.Application.Quality.Runs;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Application.Quality.Abstractions;

/// <summary>
/// Runs a quality case question through the pipeline as the given caller and returns the candidate result.
/// </summary>
public interface IQualityCaseRunner
{
    /// <summary>
    /// Runs one quality case through the pipeline as the caller and returns the candidate run.
    /// </summary>
    Task<QualityCandidateRun> RunAsync(QualityCase item, Caller caller, CancellationToken ct = default);
}
