using System.Text;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Provides the example questions the widget shows in an empty chat.
/// </summary>
public static class ExampleQuestions
{
    public const int MaxCount = 5;
    public const int MaxChars = 100;

    /// <summary>
    /// Returns the example questions for a language, preferring the Turkish list for Turkish requests.
    /// </summary>
    public static IReadOnlyList<string> For(string language, IReadOnlyList<string> general, IReadOnlyList<string> turkish)
        => language == CrmNlQuery.Domain.Common.Constants.UiLanguage.Turkish && turkish.Count > 0 ? turkish : general;

    /// <summary>
    /// Cleans the configured example questions list.
    /// </summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string>? configured)
    {
        var result = new List<string>();
        if (configured is null)
        {
            return result;
        }
        foreach (var raw in configured)
        {
            var text = OneLine(raw ?? "");
            if (text.Length == 0 || result.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            result.Add(text.Length > MaxChars ? text.Substring(0, MaxChars) : text);
            if (result.Count == MaxCount)
            {
                break;
            }
        }
        return result;
    }

    private static string OneLine(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t' || char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                {
                    sb.Append(' ');
                }
                lastSpace = true;
            }
            else if (!char.IsControl(ch))
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
