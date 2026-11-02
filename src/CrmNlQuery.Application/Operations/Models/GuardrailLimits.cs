namespace CrmNlQuery.Application.Operations.Models;

/// <summary>
/// Guardrail limits against abuse and accidental overload; defaults are restrictive and can be relaxed.
/// </summary>
public sealed record GuardrailLimits
{
    public int MaxQuestionChars { get; init; } = 1000;
    public int MaxLinkEntities { get; init; } = 4;
    public int MaxLinkDepth { get; init; } = 3;
    public int MaxConditions { get; init; } = 25;
    public int MaxRecordNameChars { get; init; } = 120;

    public bool AskEnabled { get; init; } = true;
    public int QuestionsPerMinute { get; init; } = 20;
    public int QuestionsPerDay { get; init; } = 500;
    public int MaxConcurrentPerUser { get; init; } = 2;
    public int MaxConcurrentTotal { get; init; } = 8;
    public int AskDeadlineSeconds { get; init; } = 120;
    public long CloudDailyTokenBudget { get; init; }

    /// <summary>
    /// Normalizes the limits to valid values.
    /// </summary>
    public GuardrailLimits Normalize() => this with
    {
        MaxQuestionChars = Math.Max(1, MaxQuestionChars),
        MaxLinkEntities = Math.Max(0, MaxLinkEntities),
        MaxLinkDepth = Math.Max(1, MaxLinkDepth),
        MaxConditions = Math.Max(1, MaxConditions),
        MaxRecordNameChars = Math.Max(10, MaxRecordNameChars),
        QuestionsPerMinute = Math.Max(1, QuestionsPerMinute),
        QuestionsPerDay = Math.Max(1, QuestionsPerDay),
        MaxConcurrentPerUser = Math.Max(1, MaxConcurrentPerUser),
        MaxConcurrentTotal = Math.Max(1, MaxConcurrentTotal),
        AskDeadlineSeconds = Math.Max(5, AskDeadlineSeconds),
        CloudDailyTokenBudget = Math.Max(0, CloudDailyTokenBudget),
    };
}
