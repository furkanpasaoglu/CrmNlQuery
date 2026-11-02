namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Configuration for how caller identity and group names are determined.
/// </summary>
public sealed class IdentityOptions
{
    public string Mode { get; set; } = "";
    public string FixedUser { get; set; } = "";
    public bool AllowFixedInProduction { get; set; }
    public string[] FixedGroups { get; set; } = new string[0];
}
