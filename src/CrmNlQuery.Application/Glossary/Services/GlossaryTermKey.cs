namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Builds the normalized key used to detect duplicate glossary terms.
/// </summary>
public static class GlossaryTermKey
{
    /// <summary>
    /// Returns a folded key with everything after an apostrophe removed, so a suffixed form and the bare term share a key.
    /// </summary>
    public static string Of(string term) => string.Join(' ', GlossaryMatcher.Tokens(term));
}
