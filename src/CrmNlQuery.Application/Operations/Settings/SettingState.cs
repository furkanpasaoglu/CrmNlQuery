
namespace CrmNlQuery.Application.Operations.Settings;

/// <summary>
/// Holds how a setting is shown in the panel: definition, effective value, source and default.
/// </summary>
public sealed record SettingState(SettingDefinition Definition, string Effective, string Source, string Default);
