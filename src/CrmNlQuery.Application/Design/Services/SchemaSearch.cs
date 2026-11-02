using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Searches the whole schema in code for the concepts of a design request, matching entities first and then fields of candidate entities.
/// </summary>
public static class SchemaSearch
{
    public const int MaxCandidates = 10;

    /// <summary>
    /// Represents a candidate entity matched in the schema, with its label, custom flag, score and matched terms.
    /// </summary>
    public sealed record EntityHit(string Entity, string Label, bool IsCustom, int Score, IReadOnlyList<string> Terms);

    private static readonly string[] SystemPrefixes = { "msdyn_", "adx_", "mspp_", "msft_", "mscrm_", "ms_", "powerpage", "workflow", "bot" };

    /// <summary>
    /// Tells whether a logical name is a custom table, meaning it has a prefix that is not one of Microsoft's own.
    /// </summary>
    public static bool IsCustom(string logicalName)
    {
        var name = logicalName.ToLowerInvariant();
        return name.IndexOf('_') > 0 && !SystemPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Detects the organization's publisher prefix as the most common custom table prefix, defaulting to new.
    /// </summary>
    public static string DetectPrefix(IEnumerable<string> logicalNames)
    {
        var prefix = logicalNames.Where(IsCustom).Select(n => n.Substring(0, n.IndexOf('_')).ToLowerInvariant())
            .GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return prefix ?? "new";
    }

    private static readonly string[] Suffixes = { "lari", "leri", "lar", "ler", "si", "su", "yi", "yu", "ni", "nu", "es", "i", "u", "a", "e", "s" };

    private static readonly HashSet<string> GenericWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "status", "state", "durum", "date", "tarih", "name", "type", "number", "code", "record", "kayit", "list", "liste", "time", "zaman",
        "start", "detail", "detay", "info", "bilgi", "description", "aciklama", "baslangic", "bitis"
    };

    private static readonly HashSet<string> GenericAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "statecode", "statuscode", "createdon", "modifiedon", "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby", "ownerid", "owningbusinessunit",
        "owninguser", "owningteam", "versionnumber", "importsequencenumber", "utcconversiontimezonecode", "timezoneruleversionnumber", "overriddencreatedon",
        "exchangerate", "transactioncurrencyid"
    };

    private static IEnumerable<string> Forms(string word)
    {
        yield return word;
        foreach (var suffix in Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length - suffix.Length >= 4)
            {
                yield return word.Substring(0, word.Length - suffix.Length);
            }
        }
    }

    /// <summary>
    /// Checks whether a term, or for multi-word terms a long non-generic word of it, appears in the text, trying inflected and plain forms.
    /// </summary>
    public static bool TermMatches(string term, string foldedText)
    {
        var t = NameNormalizer.Fold(term ?? "").Trim();
        if (t.Length < 4 || foldedText.Length == 0 || GenericWords.Contains(t))
        {
            return false;
        }
        if (Forms(t).Any(f => foldedText.Contains(f, StringComparison.Ordinal)))
        {
            return true;
        }
        var words = t.Split(new[] { ' ', '-', '_', '/', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            return false;
        }
        return words.Where(w => w.Length >= 5 && !GenericWords.Contains(w)).Any(w => Forms(w).Any(f => foldedText.Contains(f, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> TermsOf(DesignConcept concept)
    {
        yield return concept.Term;
        foreach (var s in concept.Synonyms)
        {
            yield return s;
        }
    }

    /// <summary>
    /// Scores entities from the table list against the design concepts, with label matches worth more than logical name matches.
    /// </summary>
    public static IReadOnlyList<EntityHit> ScoreEntities(IReadOnlyList<EntitySummary> entities, IReadOnlyList<DesignConcept> concepts, IReadOnlyList<string>? standardTables = null)
    {
        var hits = new List<EntityHit>();
        var proposed = new HashSet<string>((standardTables ?? Array.Empty<string>()).Select(n => (n ?? "").Trim().ToLowerInvariant()), StringComparer.Ordinal);
        foreach (var e in entities)
        {
            var label = NameNormalizer.Fold(e.Label ?? "");
            var logical = NameNormalizer.Fold(e.LogicalName);
            var score = 0;
            var terms = new List<string>();
            foreach (var concept in concepts)
            {
                foreach (var term in TermsOf(concept).Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var byLabel = TermMatches(term, label);
                    var byLogical = TermMatches(term, logical);
                    if (byLabel || byLogical)
                    {
                        score += byLabel ? 3 : 2;
                        terms.Add(term);
                    }
                }
            }
            if (proposed.Contains(e.LogicalName.ToLowerInvariant()))
            {
                score += 4;
                terms.Add("(standard table)");
            }
            if (score > 0)
            {
                hits.Add(new EntityHit(e.LogicalName, e.Label ?? e.LogicalName, IsCustom(e.LogicalName), score, terms.Distinct().ToList()));
            }
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Entity, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Scores fields of candidate entities against the concepts, adds to the table score and returns the best candidates up to the maximum.
    /// </summary>
    public static IReadOnlyList<SchemaMatch> Match(IReadOnlyList<EntityInfo> detailed, IReadOnlyList<EntityHit> hits, IReadOnlyList<DesignConcept> concepts)
    {
        var matches = new List<SchemaMatch>();
        foreach (var entity in detailed)
        {
            var hit = hits.FirstOrDefault(h => string.Equals(h.Entity, entity.LogicalName, StringComparison.OrdinalIgnoreCase));
            if (hit is null && !IsCustom(entity.LogicalName))
            {
                continue;
            }
            var fields = new List<MatchedField>();
            foreach (var attribute in entity.Attributes)
            {
                if (GenericAttributes.Contains(attribute.LogicalName) || attribute.AttributeType == "Virtual")
                {
                    continue;
                }
                var label = NameNormalizer.Fold(attribute.Label ?? "");
                var logical = NameNormalizer.Fold(attribute.LogicalName);
                foreach (var term in concepts.SelectMany(TermsOf).Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    if (TermMatches(term, label) || TermMatches(term, logical))
                    {
                        fields.Add(new MatchedField(attribute.LogicalName, attribute.Label ?? attribute.LogicalName, attribute.AttributeType, term));
                        break;
                    }
                }
            }
            var score = (hit?.Score ?? 0) + Math.Min(fields.Count, 5) + (fields.Count > 0 || hit is not null ? (IsCustom(entity.LogicalName) ? 2 : 0) : 0);
            if (score == 0)
            {
                continue;
            }
            var custom = entity.Attributes.Where(a => IsCustom(a.LogicalName)).Select(a => a.LogicalName).Take(30).ToList();
            matches.Add(new SchemaMatch(entity.LogicalName, entity.Label ?? entity.LogicalName, IsCustom(entity.LogicalName), entity.Attributes.Count, score,
                hit?.Terms ?? new List<string>(), fields.Take(10).ToList(), custom));
        }
        return matches.OrderByDescending(m => m.Score).ThenBy(m => m.Entity, StringComparer.Ordinal).Take(MaxCandidates).ToList();
    }
}
