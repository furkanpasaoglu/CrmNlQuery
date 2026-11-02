namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes a form context eval case, as if the user were on the form of the named record.
/// </summary>
public sealed class EvalContext
{
    public string Entity { get; set; } = "";
    public string Name { get; set; } = "";
}
