namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for relationship.
/// </summary>
public sealed class DesignRelationship
{
    public string Kind { get; set; } = "one-to-many";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string LookupField { get; set; } = "";
    public string DeleteBehavior { get; set; } = "restrict";
    public string Description { get; set; } = "";
}
