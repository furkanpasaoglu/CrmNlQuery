using System.Text.RegularExpressions;

namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Validates a model-written email template before it reaches the CRM or the user.
/// </summary>
public static class DraftValidator
{
    public const int MaxSubject = 120;
    public const int MaxBody = 2000;

    public static readonly Regex PlaceholderPattern = new Regex(@"\{\{\s*([a-z_]+)\s*\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTag = new Regex(@"<\s*/?\s*[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>
    /// Returns the problems found in an email subject and body template.
    /// </summary>
    public static IReadOnlyList<string> Validate(string? subject, string? body, IReadOnlyCollection<string> offered)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(subject))
        {
            problems.Add("the subject is empty");
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            problems.Add("the body is empty");
        }
        if (problems.Count > 0)
        {
            return problems;
        }

        if (subject!.Length > MaxSubject)
        {
            problems.Add($"the subject is longer than {MaxSubject} characters");
        }
        if (body!.Length > MaxBody)
        {
            problems.Add($"the body is longer than {MaxBody} characters");
        }
        if (subject.Contains('\n') || subject.Contains('\r'))
        {
            problems.Add("the subject must be a single line");
        }

        foreach (var (field, text) in new[] { ("subject", subject), ("body", body) })
        {
            var unknown = PlaceholderPattern.Matches(text).Select(m => m.Groups[1].Value).Where(n => !offered.Contains(n)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                problems.Add($"the {field} uses placeholders that are not available: {string.Join(", ", unknown.Select(n => "{{" + n + "}}"))}");
            }
            var rest = PlaceholderPattern.Replace(text, "");
            if (rest.Contains('{') || rest.Contains('}'))
            {
                problems.Add($"the {field} has braces that are not a valid placeholder");
            }
            if (rest.Any(char.IsDigit))
            {
                problems.Add($"the {field} contains digits outside placeholders (never write dates, amounts or numbers yourself)");
            }
            if (HtmlTag.IsMatch(text))
            {
                problems.Add($"the {field} contains HTML (plain text only)");
            }
        }
        return problems;
    }
}
