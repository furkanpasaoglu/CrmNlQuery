namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for field.
/// </summary>
public sealed class DesignField
{
    public string LogicalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public int Length { get; set; }
    public List<DesignOption> Options { get; set; } = new List<DesignOption>();
    public string Target { get; set; } = "";
    public string Description { get; set; } = "";
}
