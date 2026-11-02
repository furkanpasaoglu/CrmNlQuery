namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// Summarizes recorded model calls for reporting.
/// </summary>
public static class LlmCallSummary
{
    /// <summary>
    /// Groups call samples by purpose and summarizes each group.
    /// </summary>
    public static IReadOnlyList<PurposeSummary> ByPurpose(IReadOnlyList<LlmCallSample> samples)
    {
        return samples.GroupBy(s => s.Purpose).OrderBy(g => g.Key)
            .Select(g =>
            {
                var withInput = g.Where(s => s.InputTokens is not null).ToList();
                return new PurposeSummary(g.Key, g.Count(), g.Average(s => (double)s.EstTokens), g.Max(s => s.EstTokens),
                    withInput.Count == 0 ? 0 : withInput.Average(s => (double)s.InputTokens!.Value), g.Average(s => (double)s.Milliseconds));
            }).ToList();
    }

    /// <summary>
    /// Decides whether any call's estimated prompt would not fit the context window, or truly overflowed.
    /// </summary>
    public static bool WouldNotFit(IReadOnlyList<LlmCallSample> samples, int contextWindow)
    {
        if (contextWindow <= 0)
        {
            return false;
        }
        return samples.Any(s => s.Overflow is not null || s.EstTokens >= contextWindow - 8);
    }
}
