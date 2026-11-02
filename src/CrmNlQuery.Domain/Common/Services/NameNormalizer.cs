using System.Text;

namespace CrmNlQuery.Domain.Common.Services;

/// <summary>
/// Normalizes names for matching, culture independent and safe for Turkish letters.
/// </summary>
public static class NameNormalizer
{
    private static readonly HashSet<string> LegalTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "as", "ltd", "sti", "inc", "gmbh", "ag", "sa", "co", "kk", "sl", "llc", "corp", "limited", "anonim", "sirketi"
    };

    /// <summary>
    /// Lowercases text and folds Turkish letters by mapping each character explicitly, without culture-dependent lowering.
    /// </summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case 'İ': case 'I': case 'ı': case 'i': sb.Append('i'); break;
                case 'Ş': case 'ş': sb.Append('s'); break;
                case 'Ğ': case 'ğ': sb.Append('g'); break;
                case 'Ü': case 'ü': sb.Append('u'); break;
                case 'Ö': case 'ö': sb.Append('o'); break;
                case 'Ç': case 'ç': sb.Append('c'); break;
                default: sb.Append(char.ToLowerInvariant(ch)); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalizes a name by folding and removing legal suffixes and punctuation.
    /// </summary>
    public static string Normalize(string text)
    {
        var folded = Fold(text);
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1)
        {
            var last = tokens[tokens.Count - 1];
            if (LegalTokens.Contains(last))
            {
                tokens.RemoveAt(tokens.Count - 1);
                continue;
            }
            if (tokens.Count > 2 && last == "s" && tokens[tokens.Count - 2] == "a")
            {
                tokens.RemoveRange(tokens.Count - 2, 2);
                continue;
            }
            break;
        }
        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Builds a SQL LIKE pattern from text, mapping Turkish letters to character classes and escaping wildcards.
    /// </summary>
    public static string ToLikePattern(string text)
    {
        var sb = new StringBuilder("%");
        foreach (var ch in text.Trim())
        {
            switch (ch)
            {
                case '%': sb.Append("[%]"); break;
                case '_': sb.Append("[_]"); break;
                case '[': sb.Append("[[]"); break;
                case 'i': case 'I': case 'ı': case 'İ': sb.Append("[iIıİ]"); break;
                case 's': case 'S': case 'ş': case 'Ş': sb.Append("[sSşŞ]"); break;
                case 'g': case 'G': case 'ğ': case 'Ğ': sb.Append("[gGğĞ]"); break;
                case 'u': case 'U': case 'ü': case 'Ü': sb.Append("[uUüÜ]"); break;
                case 'o': case 'O': case 'ö': case 'Ö': sb.Append("[oOöÖ]"); break;
                case 'c': case 'C': case 'ç': case 'Ç': sb.Append("[cCçÇ]"); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('%');
        return sb.ToString();
    }
}
