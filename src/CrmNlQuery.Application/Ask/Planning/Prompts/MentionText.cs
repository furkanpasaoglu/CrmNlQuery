using System.Text.RegularExpressions;

namespace CrmNlQuery.Application.Ask.Planning.Prompts;

/// <summary>
/// Strips Turkish case and possessive suffixes from a company or person name written in a question.
/// </summary>
public static class MentionText
{
    private const string Suffixes =
        "ndaki|ndeki|daki|deki|taki|teki|nın|nin|nun|nün|ndan|nden|nda|nde|nı|ni|nu|nü|na|ne|yla|yle|ya|ye|yı|yi|yu|yü|dan|den|tan|ten|da|de|ta|te|la|le|ki|ın|in|un|ün|a|e|ı|i|u|ü";

    private static readonly Regex Suffix = new Regex(
        @"(?<=\p{L})['’‘`´](?:" + Suffixes + @")(?=$|[\s,.;:!?)])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Removes Turkish suffixes such as in Ornekova'nin to Ornekova, returning the original text if nothing would remain.
    /// </summary>
    public static string StripTurkishSuffixes(string text)
    {
        var stripped = Suffix.Replace(text, "").Trim();
        return stripped.Length == 0 ? text : stripped;
    }
}
