namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for extraction.
/// </summary>
public sealed class DesignExtraction
{
    public string Language { get; set; } = "en";
    public string Summary { get; set; } = "";
    public List<DesignConcept> Concepts { get; set; } = new List<DesignConcept>();
    public List<string> StandardTables { get; set; } = new List<string>();
}
