namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Carries a glossary entry submitted from the panel, including kind, terms and rule fields.
/// </summary>
public sealed record GlossaryEntryDto(string? Kind, List<GlossaryTermDto>? Terms, string? Entity, string? Attribute, List<GlossaryConditionDto>? Conditions, string? Description, bool? Enabled);
