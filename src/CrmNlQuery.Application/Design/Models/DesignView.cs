namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for view.
/// </summary>
public sealed class DesignView
{
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Columns { get; set; } = new List<string>();
}
