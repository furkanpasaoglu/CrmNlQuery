namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes a chained eval case whose turns run in order, each using the previous answer's query as context.
/// </summary>
public sealed class ChainCase
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    public List<EvalCase> Turns { get; set; } = new List<EvalCase>();
}
