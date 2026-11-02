using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Application.Operations.Abstractions;

/// <summary>
/// Provides the current limit values, read at each use.
/// </summary>
public interface ILimitsSource
{
    GuardrailLimits Current { get; }
}
