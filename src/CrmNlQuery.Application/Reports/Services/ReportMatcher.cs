using CrmNlQuery.Application.Reports.Models;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Reports.Services;

/// <summary>
/// Detects report-finding intent and matches reports by name, purely in code with no model and with Turkish letter folding.
/// </summary>
public static class ReportMatcher
{
    private static readonly HashSet<string> ReportWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "rapor", "raporu", "raporun", "raporunu", "rapora", "raporlar", "raporlari", "raporlarini", "raporlarimiz", "raporumuz", "report", "reports",
    };

    private static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.Ordinal)
    {
        "ac", "acar", "misin", "misiniz", "goster", "gosterir", "getir", "calistir", "bul", "bulur", "ver", "verir", "listele", "bana", "bir", "lutfen", "ilgili",
        "icin", "ile", "ve", "ya", "olan", "var", "mi", "mu", "hangi", "neler", "tum", "butun", "bu", "bunun", "sunun", "this", "that",
        "the", "a", "an", "of", "for", "to", "and", "open", "run", "show", "find", "list", "me", "my", "please", "give", "get", "all", "what", "which", "are", "there", "is",
    };

    private static readonly string[] ActionWords = { "ac", "acar", "goster", "getir", "calistir", "bul", "ver", "listele", "open", "run", "show", "find", "list", "give", "get" };

    private static readonly string[] DataPhrases = { "kac", "how many", "toplam", "ortalama", "average", "en cok", "en az", "most", "least", "sayisi", "count" };

    /// <summary>
    /// Splits text into folded word tokens.
    /// </summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        var folded = NameNormalizer.Fold(text ?? "");
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch)) { current.Append(ch); continue; }
            if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) { tokens.Add(current.ToString()); }
        return tokens;
    }

    /// <summary>
    /// Tells whether a question is a request for a report, requiring a report word plus an open or show verb or a short phrase, and canceled by counting or ranking language.
    /// </summary>
    public static bool Matches(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) { return false; }
        var tokens = Tokens(question);
        if (!tokens.Any(ReportWords.Contains)) { return false; }
        var folded = " " + string.Join(" ", tokens) + " ";
        if (DataPhrases.Any(p => folded.Contains(" " + p + " "))) { return false; }
        return tokens.Any(t => ActionWords.Contains(t)) || tokens.Count <= 6;
    }

    /// <summary>
    /// Returns the meaningful words of a question for matching report names, dropping report and filler words.
    /// </summary>
    public static IReadOnlyList<string> ContentTokens(string question)
        => Tokens(question).Where(t => !ReportWords.Contains(t) && !Filler.Contains(t)).Distinct().ToList();

    /// <summary>
    /// Finds the best matching reports, up to five, or the first ten by name when no meaningful word is given; empty when nothing matches.
    /// </summary>
    public static (IReadOnlyList<ReportItem> Items, bool ListedAll) Find(string question, IReadOnlyList<ReportItem> reports)
    {
        var content = ContentTokens(question);
        if (content.Count == 0)
        {
            return (reports.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Take(10).ToList(), true);
        }

        var scored = new List<(ReportItem Item, double Score, int NameHits)>();
        foreach (var report in reports)
        {
            var nameTokens = Tokens(report.Name).Concat(report.Keywords.SelectMany(Tokens)).ToList();
            var descTokens = Tokens(report.Description ?? "");
            var matched = 0;
            var nameHits = 0;
            var longMatch = false;
            foreach (var q in content)
            {
                var inName = nameTokens.Any(r => Same(q, r));
                var inDescription = !inName && descTokens.Any(r => Same(q, r));
                if (inName) { matched++; nameHits++; longMatch |= q.Length >= 4; }
                else if (inDescription) { matched++; longMatch |= q.Length >= 4; }
            }
            var fraction = (double)matched / content.Count;
            if (fraction >= 0.6 && longMatch) { scored.Add((report, fraction, nameHits)); }
        }
        var items = scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.NameHits).ThenBy(s => s.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(5).Select(s => s.Item).ToList();
        return (items, false);
    }

    private static bool Same(string a, string b)
    {
        if (a == b) { return true; }
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        return shorter.Length >= 4 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }
}
