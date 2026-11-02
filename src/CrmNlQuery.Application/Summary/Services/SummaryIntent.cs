using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Summary.Services;

/// <summary>
/// Recognizes a customer summary request by rule rather than by the model.
/// </summary>
public static class SummaryIntent
{
    private static readonly Regex Words = new Regex(@"\b(ozet\w*|summar\w*|overview|briefing|brief me)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks whether a question asks for a summary.
    /// </summary>
    public static bool Matches(string? question)
    {
        return !string.IsNullOrWhiteSpace(question) && Words.IsMatch(NameNormalizer.Fold(question).ToLowerInvariant());
    }

    /// <summary>
    /// Tells whether an entity can be the root of a summary.
    /// </summary>
    public static bool IsSummarizable(string entity)
        => string.Equals(entity, "account", StringComparison.OrdinalIgnoreCase) || string.Equals(entity, "contact", StringComparison.OrdinalIgnoreCase);
}
