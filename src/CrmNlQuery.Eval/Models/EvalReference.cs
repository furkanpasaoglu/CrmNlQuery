using CrmNlQuery.Application.Quality.Comparison;

namespace CrmNlQuery.Eval.Models;

/// <summary>
/// Describes the reference query mode where the correct answer is a hand-verified FetchXML compared against the candidate on current data.
/// </summary>
public sealed class EvalReference
{
    public string? FetchXml { get; set; }
    public string? File { get; set; }
    public CompareRule? Compare { get; set; }
    public List<EvalRefStep>? Steps { get; set; }
}
