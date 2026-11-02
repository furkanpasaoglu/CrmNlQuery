using CrmNlQuery.Domain.Common.Constants;

namespace CrmNlQuery.Application.Localization.Services;

/// <summary>
/// Holds the UI language of the current request, resolved by HTTP middleware so translations at the HTTP edge can read it.
/// </summary>
public static class RequestLanguage
{
    private static readonly AsyncLocal<string?> Value = new AsyncLocal<string?>();

    public static string Current => Value.Value ?? UiLanguage.Default;

    /// <summary>
    /// Sets the language of the current request.
    /// </summary>
    public static void Set(string language) => Value.Value = UiLanguage.Normalize(language);
}
