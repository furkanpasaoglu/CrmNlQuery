namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes one step of a multi-step reference query.
/// </summary>
public sealed class EvalRefStep
{
    public string? FetchXml { get; set; }
    public string? File { get; set; }
    public string Reduce { get; set; } = "all";
}
