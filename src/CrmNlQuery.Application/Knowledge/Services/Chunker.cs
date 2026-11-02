using System.Text;
using System.Text.RegularExpressions;

namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Splits a document into searchable chunks: paragraphs stay together, headings start new chunks and long paragraphs break at sentence boundaries.
/// </summary>
public static class Chunker
{
    public const int DefaultMaxChars = 800;

    /// <summary>
    /// Splits a title and body into chunks of at most the given size.
    /// </summary>
    public static IReadOnlyList<string> Split(string title, string body, int maxChars = DefaultMaxChars)
    {
        var text = (body ?? "").Replace("\r\n", "\n").Trim();
        var pieces = new List<string>();
        if (text.Length == 0)
        {
            return pieces;
        }

        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                pieces.Add(current.ToString().Trim());
                current.Clear();
            }
        }

        foreach (var paragraph in Regex.Split(text, @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (paragraph.StartsWith('#'))
            {
                Flush();
            }
            foreach (var part in paragraph.Length <= maxChars ? new[] { paragraph } : CutLong(paragraph, maxChars))
            {
                if (current.Length > 0 && current.Length + part.Length + 2 > maxChars)
                {
                    Flush();
                }
                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }
                current.Append(part);
            }
        }
        Flush();

        var header = (title ?? "").Trim();
        return pieces.Select(p => header.Length == 0 ? p : header + "\n" + p).ToList();
    }

    private static IEnumerable<string> CutLong(string paragraph, int maxChars)
    {
        var rest = paragraph;
        while (rest.Length > maxChars)
        {
            var cut = LastBreak(rest, maxChars);
            yield return rest.Substring(0, cut).Trim();
            rest = rest.Substring(cut).Trim();
        }
        if (rest.Length > 0)
        {
            yield return rest;
        }
    }

    private static int LastBreak(string text, int maxChars)
    {
        var window = text.Substring(0, maxChars);
        var sentence = Math.Max(Math.Max(window.LastIndexOf(". ", StringComparison.Ordinal), window.LastIndexOf("? ", StringComparison.Ordinal)),
            Math.Max(window.LastIndexOf("! ", StringComparison.Ordinal), window.LastIndexOf('\n')));
        if (sentence > maxChars / 3)
        {
            return sentence + 1;
        }
        var space = window.LastIndexOf(' ');
        return space > maxChars / 3 ? space : maxChars;
    }
}
