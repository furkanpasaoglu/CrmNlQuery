namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for document.
/// </summary>
public sealed class DesignDocument
{
    public string Summary { get; set; } = "";
    public List<DesignTable> Tables { get; set; } = new List<DesignTable>();
    public List<DesignRelationship> Relationships { get; set; } = new List<DesignRelationship>();
    public List<DesignView> Views { get; set; } = new List<DesignView>();
    public List<string> SecurityNotes { get; set; } = new List<string>();
    public List<string> OpenQuestions { get; set; } = new List<string>();
}
