namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes a record an account-scoped eval case depends on, so a missing record is reported as data missing rather than a failure.
/// </summary>
public sealed class EvalRequire
{
    public string Entity { get; set; } = "";
    public string Name { get; set; } = "";
}
