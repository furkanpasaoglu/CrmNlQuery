using CrmNlQuery.Application.Drafts.Models;

namespace CrmNlQuery.Application.Drafts.Services;

/// <summary>
/// Fills template placeholders with real values in a single pass without re-expanding placeholders inside values.
/// </summary>
public static class DraftFiller
{
    /// <summary>
    /// Result of filling a template: the filled text and the placeholders left unfilled.
    /// </summary>
    public sealed record Filled(string Text, IReadOnlyList<string> Unresolved);

    private static string Label(string name)
    {
        var key = "msg.ph." + name;
        var rendered = CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(key);
        return rendered != key ? rendered : (DraftPlaceholders.Find(name)?.Label ?? name);
    }

    /// <summary>
    /// Replaces placeholders with values in one pass, marking missing ones with a labelled placeholder.
    /// </summary>
    public static Filled Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        var unresolved = new List<string>();
        var text = DraftValidator.PlaceholderPattern.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            var label = "[" + Label(name) + "]";
            if (!unresolved.Contains(label))
            {
                unresolved.Add(label);
            }
            return label;
        });
        return new Filled(text, unresolved);
    }
}
