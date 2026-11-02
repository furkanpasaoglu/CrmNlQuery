using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Quality.Services;

/// <summary>
/// Pure generator that turns the schema and gathered facts into starter acceptance-set cases using fixed templates.
/// </summary>
public static class StarterSetGenerator
{
    public const int MaxListRows = 200;
    private const int MaxProbePerKind = 8;

    private static readonly HashSet<string> SystemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "versionnumber", "importsequencenumber", "timezoneruleversionnumber", "utcconversiontimezonecode", "exchangerate", "overriddencreatedon",
        "ownerid", "owninguser", "owningteam", "owningbusinessunit", "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby",
        "transactioncurrencyid", "stageid", "processid", "traversedpath", "statuscode_display",
        "quantitydecimal", "pricingdecimalprecision", "defaultuomscheduleid", "defaultuomid",
    };

    private static readonly string[] DerivedNumericParts = { "discount", "freight", "tax", "lineitem", "percentage", "rate", "count" };

    /// <summary>
    /// Tells whether a numeric attribute is a business measure worth asking about.
    /// </summary>
    public static bool BusinessNumeric(AttributeInfo attribute)
        => !DerivedNumericParts.Any(part => attribute.LogicalName.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> NumericTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Money", "Integer", "Decimal", "Double", "BigInt" };
    private static readonly HashSet<string> CategoricalTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Picklist", "State", "Status", "Boolean" };

    /// <summary>
    /// Tells whether an attribute is suitable for generated questions: labelled, non-system, non-sensitive and not derived.
    /// </summary>
    public static bool Usable(AttributeInfo attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute.Label)) { return false; }
        var name = attribute.LogicalName;
        if (SystemAttributes.Contains(name) || name.EndsWith("_base", StringComparison.OrdinalIgnoreCase)) { return false; }
        if (name.Contains("latitude", StringComparison.OrdinalIgnoreCase) || name.Contains("longitude", StringComparison.OrdinalIgnoreCase)) { return false; }
        return !SensitiveNames.IsSensitive(name, attribute.Label);
    }

    /// <summary>
    /// Returns which fields of an entity need probing, limited to lookup targets in the allowed list.
    /// </summary>
    public static StarterProbes Probes(EntityInfo entity, IReadOnlyCollection<string>? allowedTargets = null)
    {
        var categorical = entity.Attributes
            .Where(a => CategoricalTypes.Contains(a.AttributeType) && a.Options.Count >= 2 && a.Options.Count <= 30 && Usable(a))
            .Take(MaxProbePerKind).ToList();
        var numeric = entity.Attributes
            .Where(a => NumericTypes.Contains(a.AttributeType) && Usable(a) && BusinessNumeric(a))
            .OrderBy(a => a.AttributeType.Equals("Money", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(MaxProbePerKind).ToList();
        var lookups = new List<(AttributeInfo, string)>();
        foreach (var relation in entity.Relationships.Where(r => string.Equals(r.ReferencingEntity, entity.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            var attribute = entity.FindAttribute(relation.ReferencingAttribute);
            if (attribute is null || !Usable(attribute) || (allowedTargets is not null && !allowedTargets.Contains(relation.ReferencedEntity, StringComparer.OrdinalIgnoreCase))) { continue; }
            if (lookups.Any(l => l.Item1.LogicalName == attribute.LogicalName)) { continue; }
            lookups.Add((attribute, relation.ReferencedEntity));
            if (lookups.Count == 3) { break; }
        }
        return new StarterProbes(categorical, numeric, lookups);
    }

    /// <summary>
    /// Generates the starter cases for the given entities and facts.
    /// </summary>
    public static IReadOnlyList<StarterCase> Generate(IReadOnlyList<EntityInfo> entities, StarterFacts facts, string language, IReadOnlyCollection<string>? allowedTargets = null)
    {
        var tr = string.Equals(language, "tr", StringComparison.OrdinalIgnoreCase);
        var cases = new List<StarterCase>();
        foreach (var entity in entities)
        {
            if (!facts.RecordCounts.TryGetValue(entity.LogicalName, out var total) || total < 1 || string.IsNullOrEmpty(entity.PrimaryNameAttribute)) { continue; }
            var label = entity.Label;
            var n = 0;
            string Id(string template) => $"gen-{entity.LogicalName}-{template}-{++n}";

            cases.Add(new StarterCase(Id("count"), entity.LogicalName, "count",
                tr ? $"{label} kayıt sayısı kaç?" : $"How many {label} records are there?",
                $"<fetch aggregate='true'><entity name='{entity.LogicalName}'><attribute name='{entity.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>",
                "reference", "strict", null, "Toplam kayıt sayısı."));

            if (total <= MaxListRows)
            {
                cases.Add(new StarterCase(Id("list"), entity.LogicalName, "list",
                    tr ? $"Tüm {label} kayıtlarını listele" : $"List all {label} records",
                    $"<fetch><entity name='{entity.LogicalName}'><attribute name='{entity.PrimaryNameAttribute}'/></entity></fetch>",
                    "reference", "strict", "custom:" + entity.PrimaryNameAttribute, $"Küçük tablo ({total} kayıt): ad sütunu."));
            }

            var probes = Probes(entity, allowedTargets);
            var usableCategorical = probes.Categorical
                .Select(a => (Attribute: a, Counts: facts.OptionCounts.TryGetValue(Key(entity, a), out var c) ? c : null))
                .Where(x => x.Counts is not null && x.Counts.Count(kv => kv.Value > 0) >= 2)
                .OrderByDescending(x => x.Counts!.Count(kv => kv.Value > 0))
                .ToList();

            var filter = usableCategorical.FirstOrDefault();
            if (filter.Attribute is not null)
            {
                var option = filter.Attribute.Options
                    .Where(o => filter.Counts!.TryGetValue(o.Value, out var c) && c >= 1 && c <= MaxListRows)
                    .OrderByDescending(o => filter.Counts![o.Value]).FirstOrDefault();
                if (option is not null)
                {
                    cases.Add(new StarterCase(Id("filter"), entity.LogicalName, "filter",
                        tr ? $"{filter.Attribute.Label} alanı {option.Label} olan {label} kayıtlarını getir" : $"Show {label} records where {filter.Attribute.Label} is {option.Label}",
                        $"<fetch><entity name='{entity.LogicalName}'><attribute name='{entity.PrimaryNameAttribute}'/><filter><condition attribute='{filter.Attribute.LogicalName}' operator='eq' value='{option.Value}'/></filter></entity></fetch>",
                        "reference", "strict", "custom:" + entity.PrimaryNameAttribute, $"{filter.Attribute.LogicalName} = {option.Value} ({filter.Counts![option.Value]} kayıt)."));
                }
            }

            var breakdown = usableCategorical.FirstOrDefault(x => filter.Attribute is null || x.Attribute.LogicalName != filter.Attribute.LogicalName);
            if (breakdown.Attribute is null) { breakdown = filter; }
            if (breakdown.Attribute is not null)
            {
                cases.Add(new StarterCase(Id("breakdown"), entity.LogicalName, "breakdown",
                    tr ? $"{label} kayıtlarının {breakdown.Attribute.Label} dağılımını grafik olarak göster" : $"Show the distribution of {label} records by {breakdown.Attribute.Label} as a chart",
                    $"<fetch aggregate='true'><entity name='{entity.LogicalName}'><attribute name='{breakdown.Attribute.LogicalName}' groupby='true' alias='g'/><attribute name='{entity.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>",
                    "last", "strict", null, $"{breakdown.Attribute.LogicalName} kırılımı; yalnız sayılar karşılaştırılır."));
            }

            var numeric = probes.Numeric
                .Select(a => (Attribute: a, Filled: facts.FilledCounts.TryGetValue(Key(entity, a), out var f) ? f : 0))
                .Where(x => x.Filled >= Math.Min(10, Math.Max(5, total / 2)))
                .OrderByDescending(x => x.Filled).FirstOrDefault();
            if (numeric.Attribute is not null)
            {
                var a = numeric.Attribute;
                cases.Add(new StarterCase(Id("top"), entity.LogicalName, "top",
                    tr ? $"{label} kayıtlarından {a.Label} değeri en yüksek 5 tanesini getir" : $"Show the 5 {label} records with the highest {a.Label}",
                    $"<fetch top='5'><entity name='{entity.LogicalName}'><attribute name='{entity.PrimaryNameAttribute}'/><filter><condition attribute='{a.LogicalName}' operator='not-null'/></filter><order attribute='{a.LogicalName}' descending='true'/></entity></fetch>",
                    "reference", "boundary", $"custom:{entity.PrimaryNameAttribute}, {a.LogicalName}", $"{a.LogicalName} en yüksek 5; sınırdaki eşitlikler kabul."));
                cases.Add(new StarterCase(Id("bottom"), entity.LogicalName, "bottom",
                    tr ? $"{label} kayıtlarından {a.Label} değeri en düşük 3 tanesini getir" : $"Show the 3 {label} records with the lowest {a.Label}",
                    $"<fetch top='3'><entity name='{entity.LogicalName}'><attribute name='{entity.PrimaryNameAttribute}'/><filter><condition attribute='{a.LogicalName}' operator='not-null'/></filter><order attribute='{a.LogicalName}'/></entity></fetch>",
                    "reference", "boundary", $"custom:{entity.PrimaryNameAttribute}, {a.LogicalName}", $"{a.LogicalName} en düşük 3 (boş değerler hariç); sınırdaki eşitlikler kabul."));
            }

            foreach (var (attribute, target) in probes.Lookups)
            {
                if (!facts.GroupCounts.TryGetValue(Key(entity, attribute), out var groups) || groups < 2 || groups > 50) { continue; }
                cases.Add(new StarterCase(Id("lookup"), entity.LogicalName, "lookup",
                    tr ? $"{label} kayıtlarının sayısını {attribute.Label} alanına göre göster" : $"Show the number of {label} records per {attribute.Label}",
                    $"<fetch aggregate='true'><entity name='{entity.LogicalName}'><attribute name='{attribute.LogicalName}' groupby='true' alias='g'/><attribute name='{entity.PrimaryIdAttribute}' aggregate='count' alias='n'/></entity></fetch>",
                    "last", "strict", null, $"{attribute.LogicalName} ({target}) başına sayı; {groups} grup."));
                break;
            }
        }
        return cases;
    }

    /// <summary>
    /// Builds a unique key for an entity and attribute pair.
    /// </summary>
    public static string Key(EntityInfo entity, AttributeInfo attribute) => entity.LogicalName + "." + attribute.LogicalName;

}
