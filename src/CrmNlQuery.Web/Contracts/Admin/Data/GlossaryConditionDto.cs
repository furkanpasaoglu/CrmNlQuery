namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Carries one glossary rule condition with attribute, operator and values.
/// </summary>
public sealed record GlossaryConditionDto(string? Attribute, string? Operator, List<string>? Values);
