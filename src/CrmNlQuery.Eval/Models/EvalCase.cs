namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes one eval case: a question with its expectations.
/// </summary>
public sealed class EvalCase
{
    public EvalContext? Context { get; set; }
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public bool? Generated { get; set; }
    public bool? Verified { get; set; }
    public string? Template { get; set; }
    public Dictionary<string, string>? Answers { get; set; }
    public int? PickChoice { get; set; }
    public string? Note { get; set; }
    public bool? MultiStep { get; set; }
    public EvalExpect Expect { get; set; } = new EvalExpect();
    public List<EvalRequire>? Requires { get; set; }
}
