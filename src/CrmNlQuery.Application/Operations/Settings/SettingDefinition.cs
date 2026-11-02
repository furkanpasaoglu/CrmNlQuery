
namespace CrmNlQuery.Application.Operations.Settings;

/// <summary>
/// Defines one setting manageable from the panel: key, kind, allowed range, code default and description.
/// </summary>
public sealed record SettingDefinition(string Key, SettingKind Kind, decimal Min, decimal Max, string Default, string Group, string Description, IReadOnlyList<string>? Choices = null);
