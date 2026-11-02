namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Carries one glossary term with its text and language.
/// </summary>
public sealed record GlossaryTermDto(string? Text, string? Language);
