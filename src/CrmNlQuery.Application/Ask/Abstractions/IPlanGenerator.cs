using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Generates a plan from a plan request.
/// </summary>
public interface IPlanGenerator
{
    /// <summary>
    /// Generates a plan and returns the outcome.
    /// </summary>
    Task<PlanOutcome> GenerateAsync(PlanRequest request, CancellationToken ct = default);
}
