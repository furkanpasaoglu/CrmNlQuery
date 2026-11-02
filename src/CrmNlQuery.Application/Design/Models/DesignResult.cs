namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for result.
/// </summary>
public sealed class DesignResult
{
    public string Language { get; set; } = "en";
    public string Prefix { get; set; } = "new";
    public string Request { get; set; } = "";
    public DesignDocument Document { get; set; } = new DesignDocument();
    public IReadOnlyList<SchemaMatch> Matches { get; set; } = Array.Empty<SchemaMatch>();
    public IReadOnlyList<ReuseDecision> Decisions { get; set; } = Array.Empty<ReuseDecision>();
    public IReadOnlyList<string> Problems { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ClosedTables { get; set; } = Array.Empty<string>();
}
