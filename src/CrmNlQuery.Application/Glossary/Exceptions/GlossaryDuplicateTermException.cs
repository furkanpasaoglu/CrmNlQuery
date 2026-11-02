namespace CrmNlQuery.Application.Glossary.Exceptions;

/// <summary>
/// Signals that a glossary term already exists in another entry after folding and removing apostrophe suffixes.
/// </summary>
public sealed class GlossaryDuplicateTermException : Exception
{
    public string Term { get; }
    public string OtherEntryId { get; }

    public GlossaryDuplicateTermException(string term, string otherEntryId) : base($"The term '{term}' is already defined.")
    {
        Term = term;
        OtherEntryId = otherEntryId;
    }
}
