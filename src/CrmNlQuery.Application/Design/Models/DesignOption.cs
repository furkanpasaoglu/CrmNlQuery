namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for option.
/// </summary>
public sealed class DesignOption
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}
