namespace CrmNlQuery.Domain.Common.Constants;

/// <summary>
/// Defines the supported interface languages; adding a language means adding a code and catalogs.
/// </summary>
public static class UiLanguage
{
    public const string English = "en";
    public const string Turkish = "tr";
    public const string Default = English;

    public static readonly IReadOnlyList<string> All = new[] { English, Turkish };

    /// <summary>
    /// Parses a language value such as tr-TR into a supported code, or null if unsupported or empty.
    /// </summary>
    public static string? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var primary = value.Trim().ToLowerInvariant();
        var dash = primary.IndexOfAny(new[] { '-', '_' });
        if (dash > 0)
        {
            primary = primary.Substring(0, dash);
        }
        return All.Contains(primary) ? primary : null;
    }

    /// <summary>
    /// Normalizes a language value to a supported code, falling back to the default.
    /// </summary>
    public static string Normalize(string? value) => Parse(value) ?? Default;

    /// <summary>
    /// Picks the highest-quality supported language from an Accept-Language header, or null.
    /// </summary>
    public static string? FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }
        string? best = null;
        var bestQuality = -1.0;
        foreach (var part in header.Split(','))
        {
            var pieces = part.Split(';');
            var language = Parse(pieces[0]);
            if (language is null)
            {
                continue;
            }
            var quality = 1.0;
            foreach (var parameter in pieces.Skip(1))
            {
                var p = parameter.Trim();
                if (p.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(p.Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var q))
                {
                    quality = q;
                }
            }
            if (quality > bestQuality)
            {
                best = language;
                bestQuality = quality;
            }
        }
        return bestQuality > 0 ? best : null;
    }
}
