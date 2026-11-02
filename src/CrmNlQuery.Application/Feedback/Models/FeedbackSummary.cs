namespace CrmNlQuery.Application.Feedback.Models;

/// <summary>
/// Summarizes feedback votes with counts, reasons and daily series.
/// </summary>
public sealed record FeedbackSummary(int Up, int Down, IReadOnlyList<FeedbackCount> Reasons, IReadOnlyList<FeedbackCount> DownTables,
    int DownSingleStep, int DownMultiStep, IReadOnlyList<FeedbackDay> Days);
