using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Decides by rules, without a model, whether a message is a record creation request such as creating an account.
/// </summary>
public static class CreateIntent
{
    private static readonly Regex Tokens = new Regex(@"[^\p{L}\p{N}_]+", RegexOptions.Compiled);

    private static readonly string[] VerbPrefixes = { "olustur", "yarat", "create", "kaydedin" };
    private static readonly HashSet<string> VerbWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "ekle", "ekleyin", "ekleyelim", "add", "insert", "kaydet",
    };

    private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["account"] = new[] { "hesap", "hesab", "firma", "sirket", "musteri" },
        ["contact"] = new[] { "kisi", "irtibat" },
        ["lead"] = new[] { "aday" },
        ["opportunity"] = new[] { "firsat" },
        ["incident"] = new[] { "vaka", "talep" },
        ["task"] = new[] { "gorev" },
        ["product"] = new[] { "urun" },
        ["salesorder"] = new[] { "siparis" },
    };

    /// <summary>
    /// Returns the common alternative names of an entity shown to the model and the user.
    /// </summary>
    public static IReadOnlyList<string> AliasesOf(string entity)
        => Aliases.TryGetValue(entity, out var a) ? a : Array.Empty<string>();

    /// <summary>
    /// Checks whether the text contains a creation verb.
    /// </summary>
    public static bool HasVerb(string text)
    {
        var words = Tokens.Split(NameNormalizer.Fold(text ?? "")).Where(w => w.Length > 0);
        return words.Any(w => VerbWords.Contains(w) || VerbPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Returns the writable tables mentioned in the sentence by name, label or Turkish equivalent.
    /// </summary>
    public static IReadOnlyList<CreateSchema> Mentioned(string text, IReadOnlyList<CreateSchema> writable)
    {
        var words = Tokens.Split(NameNormalizer.Fold(text ?? "")).Where(w => w.Length > 0).ToList();
        return writable.Where(schema => NamesOf(schema).Any(stem => words.Any(w => w.Length >= 3 && w.StartsWith(stem, StringComparison.Ordinal)))).ToList();
    }

    /// <summary>
    /// Checks whether a word, such as an entity value in JSON, names the given table by logical name, label, plural or Turkish stem.
    /// </summary>
    public static bool Names(CreateSchema schema, string word)
    {
        var folded = NameNormalizer.Fold(word ?? "").Trim();
        return folded.Length >= 3 && NamesOf(schema).Any(stem => folded.StartsWith(stem, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks whether the text is a creation request mentioning one of the writable tables.
    /// </summary>
    public static bool Matches(string text, IReadOnlyList<CreateSchema> writable)
    {
        if (string.IsNullOrWhiteSpace(text) || writable.Count == 0)
        {
            return false;
        }
        var words = Tokens.Split(NameNormalizer.Fold(text)).Where(w => w.Length > 0).ToList();
        var hasVerb = words.Any(w => VerbWords.Contains(w) || VerbPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
        if (!hasVerb)
        {
            return false;
        }
        return writable.Any(schema => NamesOf(schema).Any(stem => words.Any(w => w.Length >= 3 && w.StartsWith(stem, StringComparison.Ordinal))));
    }

    private static IEnumerable<string> NamesOf(CreateSchema schema)
    {
        foreach (var name in new[] { schema.Entity, schema.Label })
        {
            var folded = NameNormalizer.Fold(name);
            if (folded.Length >= 3)
            {
                yield return folded;
            }
        }
        foreach (var alias in AliasesOf(schema.Entity))
        {
            yield return alias;
        }
    }
}
