namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for concept.
/// </summary>
public sealed class DesignConcept
{
    public string Term { get; set; } = "";
    public List<string> Synonyms { get; set; } = new List<string>();
    public string Kind { get; set; } = "table";
}
