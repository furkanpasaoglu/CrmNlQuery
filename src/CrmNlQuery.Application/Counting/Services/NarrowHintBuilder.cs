using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Counting.Services;

/// <summary>
/// Builds model-free suggestions for narrowing a truncated result from the entity metadata, never from row data.
/// </summary>
public static class NarrowHintBuilder
{
    public const int MaxHints = 3;
    public const int MaxOptions = 5;
    private const int MaxOptionSetSize = 8;

    private static readonly HashSet<string> SystemAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "versionnumber", "importsequencenumber", "utcconversiontimezonecode", "timezoneruleversionnumber", "exchangerate",
        "processid", "stageid", "traversedpath", "modifiedon", "overriddencreatedon"
    };

    /// <summary>
    /// Returns narrowing hints for a truncated result from the entity's status, option, city and date fields.
    /// </summary>
    public static IReadOnlyList<NarrowHint> For(string fetchXml, EntityInfo entity)
    {
        XElement fetch;
        try
        {
            fetch = XDocument.Parse(fetchXml).Root!;
        }
        catch (XmlException)
        {
            return new List<NarrowHint>();
        }
        if (string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new List<NarrowHint>();
        }

        var filtered = new HashSet<string>(
            fetch.Descendants("condition").Select(c => (string?)c.Attribute("attribute")).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!),
            StringComparer.OrdinalIgnoreCase);

        var candidates = entity.Attributes.Where(a => Usable(a, entity, filtered)).ToList();

        var optionSets = candidates.Where(a => IsOptionSet(a)).OrderBy(a => Rank(a)).ToList();
        var statusFields = optionSets.Where(a => Rank(a) != 1).Take(2).Select(ToOptionsHint);
        var otherOptionSets = optionSets.Where(a => Rank(a) == 1).Select(ToOptionsHint);

        var cities = candidates
            .Where(a => a.AttributeType == "String" && a.LogicalName.Contains("city", StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .Select(a => new NarrowHint(a.LogicalName, a.Label, "text", new List<string>()));

        var dates = candidates
            .Where(a => a.AttributeType == "DateTime")
            .OrderBy(a => string.Equals(a.LogicalName, "createdon", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(1)
            .Select(a => new NarrowHint(a.LogicalName, a.Label, "date", new List<string>()));

        return statusFields.Concat(cities).Concat(dates).Concat(otherOptionSets).Take(MaxHints).ToList();
    }

    private static NarrowHint ToOptionsHint(AttributeInfo a)
    {
        return new NarrowHint(a.LogicalName, a.Label, "options", a.Options.Select(o => o.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Take(MaxOptions).ToList());
    }

    /// <summary>
    /// Checks whether an attribute may be suggested for narrowing, excluding system and already filtered fields.
    /// </summary>
    internal static bool Usable(AttributeInfo a, EntityInfo entity, HashSet<string> filtered)
    {
        var name = a.LogicalName;
        return !filtered.Contains(name)
            && !string.Equals(name, entity.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, entity.PrimaryNameAttribute, StringComparison.OrdinalIgnoreCase)
            && !SystemAttributes.Contains(name)
            && !name.EndsWith("_base", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("yomi", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(a.Label);
    }

    /// <summary>
    /// Returns whether the attribute is a single-choice option set with labels, excluding booleans and multi-select.
    /// </summary>
    internal static bool IsOptionSet(AttributeInfo a)
    {
        var typed = a.AttributeType == "Picklist" || a.AttributeType == "State" || a.AttributeType == "Status";
        var labels = a.Options.Count(o => !string.IsNullOrWhiteSpace(o.Label));
        return typed && labels >= 2 && labels <= MaxOptionSetSize;
    }

    /// <summary>
    /// Returns the suggestion priority of an attribute, with status fields first.
    /// </summary>
    internal static int Rank(AttributeInfo a)
    {
        if (string.Equals(a.LogicalName, "statecode", StringComparison.OrdinalIgnoreCase)) { return 0; }
        if (string.Equals(a.LogicalName, "statuscode", StringComparison.OrdinalIgnoreCase)) { return 2; }
        return 1;
    }
}
