namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Carries the status, total and limit flag of a record count.
/// </summary>
public sealed record CountResult(CountStatus Status, long Total = 0, bool LimitExceeded = false, string EntityLabel = "");
