namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Holds the result of saving a glossary entry with status, reason and entry id.
/// </summary>
public sealed record GlossarySaveResult(GlossarySaveStatus Status, string? Reason = null, string? EntryId = null);
