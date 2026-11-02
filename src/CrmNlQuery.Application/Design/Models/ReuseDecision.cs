namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Model decision for one candidate table in the design assistant: extend it, reuse it as is, or ignore it.
/// </summary>
public sealed class ReuseDecision
{
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "ignore";
    public string Reason { get; set; } = "";
}
