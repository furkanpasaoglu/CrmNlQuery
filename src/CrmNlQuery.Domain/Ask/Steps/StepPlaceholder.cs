using System.Text.RegularExpressions;

namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// Handles the step placeholder the model writes in dependent multi-step plans.
/// </summary>
public static class StepPlaceholder
{
    private static readonly Regex Pattern = new Regex(@"^\{\{step([1-9][0-9]*)\}\}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Formats the placeholder text for a step number.
    /// </summary>
    public static string Format(int step)
    {
        return "{{step" + step + "}}";
    }

    /// <summary>
    /// Tries to read the step number from placeholder text.
    /// </summary>
    public static bool TryParse(string? text, out int step)
    {
        step = 0;
        if (text is null)
        {
            return false;
        }
        var match = Pattern.Match(text.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, out step);
    }

    /// <summary>
    /// Checks whether text is a step placeholder.
    /// </summary>
    public static bool IsPlaceholder(string? text)
    {
        return TryParse(text, out _);
    }
}
