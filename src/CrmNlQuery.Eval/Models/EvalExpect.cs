namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes the expected outcome of an eval case.
/// </summary>
public sealed class EvalExpect
{
    public string? FirstKind { get; set; }
    public string? FirstKeyPrefix { get; set; }
    public string FinalKind { get; set; } = "answer";
    public string? Entity { get; set; }
    public List<string>? Columns { get; set; }
    public List<string>? MustContain { get; set; }
    public List<string>? MustNotContain { get; set; }
    public List<string>? MustContainAny { get; set; }
    public List<string>? MustNotUse { get; set; }
    public List<string>? MustUseFetch { get; set; }
    public List<string>? MustNotUseFetch { get; set; }
    public int? MinRows { get; set; }
    public int? RowCount { get; set; }
    public bool? FollowUp { get; set; }
    public bool? UsesContext { get; set; }
    public EvalReference? Reference { get; set; }
}
