using System.Text;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Pure, model-free glossary matcher that finds terms in a question using letter and accent folding, apostrophe suffix stripping and word-start matching.
/// </summary>
public static class GlossaryMatcher
{
    public const int MaxHits = 8;
    private const int MinStem = 4;

    private static readonly System.Text.RegularExpressions.Regex SuffixChain = new System.Text.RegularExpressions.Regex(
        "^(?:lar|ler)?" +
        "(?:imiz|umuz|iniz|unuz|miz|muz|niz|nuz|im|um|in|un|iz|uz|si|su|m|n|i|u)?" +
        "(?:ndaki|ndeki|daki|deki|taki|teki|ndan|nden|yla|yle|nda|nde|dan|den|tan|ten|nin|nun|yi|yu|ni|nu|ya|ye|na|ne|da|de|ta|te|la|le|ki|ca|ce|li|lu|siz|suz|dir|tir|dur|tur|a|e|i|u|in|un)?$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private sealed record Span(int Start, int Length);

    /// <summary>
    /// Returns the glossary entries whose terms appear in the question, as match hits.
    /// </summary>
    public static IReadOnlyList<GlossaryHit> Match(string question, IReadOnlyList<GlossaryEntry> entries)
    {
        var words = Tokens(question);
        if (words.Count == 0 || entries.Count == 0)
        {
            return new List<GlossaryHit>();
        }

        var candidates = new List<(GlossaryEntry Entry, string Phrase, Span Span, int Weight)>();
        foreach (var entry in entries.Where(e => e.Enabled))
        {
            foreach (var term in entry.Terms)
            {
                var termWords = Tokens(term.Text);
                if (termWords.Count == 0)
                {
                    continue;
                }
                var at = Find(words, termWords);
                if (at >= 0)
                {
                    candidates.Add((entry, term.Text.Trim(), new Span(at, termWords.Count), termWords.Count * 1000 + termWords.Sum(w => w.Length)));
                    break;
                }
            }
        }

        var chosen = new List<(GlossaryEntry Entry, string Phrase, Span Span, int Weight)>();
        foreach (var c in candidates.OrderByDescending(c => c.Weight).ThenBy(c => c.Span.Start))
        {
            if (chosen.Count >= MaxHits) { break; }
            if (chosen.Any(x => Overlaps(x.Span, c.Span))) { continue; }
            chosen.Add(c);
        }
        return chosen.Select(c => new GlossaryHit(c.Entry, c.Phrase)).ToList();
    }

    /// <summary>
    /// Checks whether two terms are equivalent for glossary matcher.
    /// </summary>
    public static bool Equivalent(string a, string b)
    {
        var x = Tokens(a);
        var y = Tokens(b);
        if (x.Count == 0 || x.Count != y.Count) { return false; }
        for (var i = 0; i < x.Count; i++)
        {
            if (!WordMatches(x[i], y[i]) && !WordMatches(y[i], x[i])) { return false; }
        }
        return true;
    }

    private static bool Overlaps(Span a, Span b) => a.Start < b.Start + b.Length && b.Start < a.Start + a.Length;

    private static int Find(IReadOnlyList<string> words, IReadOnlyList<string> term)
    {
        for (var i = 0; i + term.Count <= words.Count; i++)
        {
            var ok = true;
            for (var j = 0; j < term.Count && ok; j++)
            {
                ok = WordMatches(words[i + j], term[j]);
            }
            if (ok) { return i; }
        }
        return -1;
    }

    private static bool WordMatches(string word, string term)
    {
        if (term.Length < MinStem)
        {
            return word == term;
        }
        if (EnglishPlural(word, term)) { return true; }
        if (!word.StartsWith(term, StringComparison.Ordinal))
        {
            return false;
        }
        var extra = word.Substring(term.Length);
        return SuffixChain.IsMatch(extra);
    }

    private static bool EnglishPlural(string word, string term)
    {
        if (word == term + "s" || word == term + "es") { return true; }
        return term.EndsWith('y') && word == term.Substring(0, term.Length - 1) + "ies";
    }

    /// <summary>
    /// Splits text into tokens for glossary matcher.
    /// </summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        var folded = NameNormalizer.Fold(text ?? "");
        var result = new List<string>();
        var current = new StringBuilder();
        var cut = false;
        void Flush()
        {
            if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            cut = false;
        }
        foreach (var ch in folded)
        {
            if (ch is '\'' or '’' or '‘' or '`' or '´')
            {
                if (current.Length > 0) { cut = true; }
                continue;
            }
            if (char.IsLetterOrDigit(ch))
            {
                if (!cut) { current.Append(ch); }
            }
            else
            {
                Flush();
            }
        }
        Flush();
        return result;
    }
}
