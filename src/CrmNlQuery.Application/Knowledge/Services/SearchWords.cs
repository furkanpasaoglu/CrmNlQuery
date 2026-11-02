using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Extracts the query words used in keyword search, shared by the memory and PostgreSQL stores.
/// </summary>
public static class SearchWords
{
    public const int MaxWords = 30;

    private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.Ordinal)
    {
        "getir", "getirir", "goster", "gosterir", "listele", "listeler", "ver", "verir", "bul", "bulur", "bana", "bize", "icin", "olan", "olanlari",
        "bir", "her", "bunlar", "tum", "butun", "hepsi", "hangi", "hangileri", "nedir", "kac", "kadar", "gibi", "daha", "ile", "ama", "veya", "ancak", "lutfen",
        "show", "list", "get", "give", "find", "tell", "all", "the", "and", "which", "what", "who", "how", "many", "are", "for", "with",
        "from", "that", "this", "have", "has", "does", "can", "please", "our", "your", "me", "any", "each", "every",
    };

    /// <summary>
    /// Extracts search words from a question, dropping command words.
    /// </summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        return Regex.Matches(text ?? "", @"[\p{L}\p{N}_]{3,}")
            .Select(m => m.Value)
            .Where(w => !Stop.Contains(NameNormalizer.Fold(w)))
            .GroupBy(w => NameNormalizer.Fold(w))
            .Select(g => g.First())
            .Take(MaxWords)
            .ToList();
    }
}
