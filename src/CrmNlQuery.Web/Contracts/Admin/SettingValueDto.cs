namespace CrmNlQuery.Web.Contracts.Admin;

/// <summary>
/// Request body carrying a new setting value.
/// </summary>
public sealed record SettingValueDto(string? Value);
