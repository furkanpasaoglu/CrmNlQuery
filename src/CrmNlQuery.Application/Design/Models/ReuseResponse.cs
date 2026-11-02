namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Structured model response for the reuse-decision step, holding one decision per candidate table.
/// </summary>
public sealed class ReuseResponse
{
    public List<ReuseDecision> Decisions { get; set; } = new List<ReuseDecision>();
}
