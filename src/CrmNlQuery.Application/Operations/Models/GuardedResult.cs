using CrmNlQuery.Application.Ask.Models;

namespace CrmNlQuery.Application.Operations.Models;

/// <summary>
/// Result of a guarded ask: either a denial from the gate or an outcome, which is null if the rerun was not found.
/// </summary>
public sealed record GuardedResult(AskOutcome? Outcome, GateDenial? Denial);
