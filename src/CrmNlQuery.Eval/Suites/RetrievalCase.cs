namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Describes one retrieval evaluation case with a question and expected document.
/// </summary>
public sealed class RetrievalCase
{
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public List<string> Expect { get; set; } = new List<string>();
    public List<string>? ExpectText { get; set; }
    public string? Note { get; set; }
}
