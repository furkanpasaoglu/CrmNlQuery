namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Result of a glossary admin operation, with success or a short reason code the panel translates plus optional warnings.
/// </summary>
public sealed record GlossaryAdminResult(bool Ok, string? Reason = null, string? EntryId = null, string? Term = null, IReadOnlyList<string>? Documents = null)
{
    /// <summary>
    /// Creates a successful glossary admin result with an optional entry id and warnings.
    /// </summary>
    public static GlossaryAdminResult Success(string? id = null, IReadOnlyList<string>? documents = null) => new GlossaryAdminResult(true, null, id, null, documents);
    /// <summary>
    /// Creates a failed glossary admin result with a reason code and optional detail.
    /// </summary>
    public static GlossaryAdminResult Fail(string reason, string? term = null) => new GlossaryAdminResult(false, reason, null, term);
}
