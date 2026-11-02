namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for table.
/// </summary>
public sealed class DesignTable
{
    public string LogicalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PluralName { get; set; } = "";
    public string Ownership { get; set; } = "user";
    public string Description { get; set; } = "";
    public string Extends { get; set; } = "";
    public string PrimaryNameLogical { get; set; } = "";
    public string PrimaryNameDisplay { get; set; } = "";
    public List<DesignField> Fields { get; set; } = new List<DesignField>();
}
