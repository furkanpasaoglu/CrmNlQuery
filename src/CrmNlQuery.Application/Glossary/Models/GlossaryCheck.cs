namespace CrmNlQuery.Application.Glossary.Models;

/// <summary>
/// Result of a glossary entry check, either valid or failed with a reason code.
/// </summary>
public sealed record GlossaryCheck(bool Ok, string? Reason = null)
{
    public static readonly GlossaryCheck Pass = new GlossaryCheck(true);
    /// <summary>
    /// Creates a failed glossary check with a reason code.
    /// </summary>
    public static GlossaryCheck Fail(string reason) => new GlossaryCheck(false, reason);
}
