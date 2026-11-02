namespace CrmNlQuery.Application.DataPolicy.Models;

/// <summary>
/// Carries the outcome of an access probe (readable, not readable or error) with a message that never contains row data.
/// </summary>
public sealed record DataPolicyProbeResult(string Outcome, string Message);
