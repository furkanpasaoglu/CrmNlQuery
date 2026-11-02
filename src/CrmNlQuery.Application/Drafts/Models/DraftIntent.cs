using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Recognizes an email draft request by rules, not the model: an email word plus a writing word.
/// </summary>
public static class DraftIntent
{
    public const string Formal = "formal";
    public const string Warm = "warm";
    public const string Short = "short";

    private static readonly Regex MailWords = new Regex(@"\b(e-?posta\w*|e-?mail\w*|mail\w*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WriteWords = new Regex(@"\b(yaz\w*|hazirla\w*|taslak\w*|olustur\w*|gonder\w*|draft\w*|write|compose|prepare|send|follow-?up)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex QueryWords = new Regex(@"\b(getir\w*|listele\w*|goster\w*|bul(?:un\w*|ur\w*)?|adres\w*|yazdir\w*|gonderil\w*|gelen|show|list|find|which|hangi\w*|how many|address\w*|sent)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShortWords = new Regex(@"\b(kisa\w*|short\w*|brief\w*|concise\w*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WarmWords = new Regex(@"\b(sicak\w*|samimi\w*|friendly|warm\w*|casual\w*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Tells whether a question is an email draft request.
    /// </summary>
    public static bool Matches(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }
        var text = NameNormalizer.Fold(question).ToLowerInvariant();
        return MailWords.IsMatch(text) && WriteWords.IsMatch(text) && !QueryWords.IsMatch(text);
    }

    /// <summary>
    /// Reads the tone (short, warm or formal) from the question by rules.
    /// </summary>
    public static string ToneOf(string? question)
    {
        var text = NameNormalizer.Fold(question ?? "").ToLowerInvariant();
        if (ShortWords.IsMatch(text))
        {
            return Short;
        }
        return WarmWords.IsMatch(text) ? Warm : Formal;
    }
}
