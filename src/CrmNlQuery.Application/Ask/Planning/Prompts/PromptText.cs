using System.Globalization;
using System.Text;

namespace CrmNlQuery.Application.Ask.Planning.Prompts;

/// <summary>
/// Cleans CRM-originated text such as record names and labels before it is placed in a model prompt, defending against injected instructions.
/// </summary>
public static class PromptText
{
    /// <summary>
    /// Cleans text by turning control characters into spaces, removing format characters and cutting to the maximum length.
    /// </summary>
    public static string Clean(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var sb = new StringBuilder(Math.Min(text.Length, maxChars + 1));
        var lastSpace = true;
        foreach (var ch in text)
        {
            var category = char.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Format)
            {
                continue;
            }
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
                continue;
            }
            sb.Append(ch);
            lastSpace = false;
        }
        var clean = sb.ToString().TrimEnd();
        return clean.Length <= maxChars ? clean : clean.Substring(0, Math.Max(0, maxChars - 1)).TrimEnd() + "…";
    }

    /// <summary>
    /// Cleans text and converts double quotes to single quotes so it can be quoted safely in a prompt.
    /// </summary>
    public static string Quote(string? text, int maxChars) => Clean(text, maxChars).Replace('"', '\'');
}
