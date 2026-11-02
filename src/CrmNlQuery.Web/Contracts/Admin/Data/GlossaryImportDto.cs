namespace CrmNlQuery.Web.Contracts.Admin.Data;

/// <summary>
/// Carries a glossary JSON import with an optional apply flag.
/// </summary>
public sealed record GlossaryImportDto(string? Json, bool? Apply);
