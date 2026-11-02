using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// Decides when the decomposer model call is needed, always for conditional wording and for ranking wording only when related entities exist.
/// </summary>
public static class StepRouter
{
    /// <summary>
    /// Tells whether a question needs the decomposer given its wording and analysis.
    /// </summary>
    public static bool NeedsDecomposer(string question, QuestionAnalysis analysis)
    {
        return ConditionalQuestionFilter.Classify(question) switch
        {
            StepTrigger.Always => true,
            StepTrigger.OnlyRanking => analysis.RelatedEntities.Count > 0,
            _ => false,
        };
    }
}
