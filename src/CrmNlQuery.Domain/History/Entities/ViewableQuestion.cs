namespace CrmNlQuery.Domain.History.Entities;

/// <summary>
/// A question made viewable to administrators: text, time and root table, marked at ask time.
/// </summary>
public sealed record ViewableQuestion(DateTimeOffset At, string Question, string Entity);
