using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Suggestions.Services;

/// <summary>
/// Builds follow-up suggestions under an answer in code from the executed FetchXML and filtered schema.
/// </summary>
public static class SuggestionBuilder
{
    public const int Max = 4;

    private static readonly string[] CommonRelated = { "opportunity", "incident", "contact", "salesorder", "contract", "quote", "invoice" };

    private static readonly string[] NotGroupable = { "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby", "owningbusinessunit", "owninguser", "owningteam" };

    /// <summary>
    /// Builds up to four follow-up suggestions for a list result.
    /// </summary>
    public static IReadOnlyList<Suggestion> For(string fetchXml, EntityInfo entity, ResultTable table, bool truncated, IReadOnlyList<EntitySummary> entities)
    {
        XElement fetch;
        try
        {
            fetch = XDocument.Parse(fetchXml).Root!;
        }
        catch (XmlException)
        {
            return new List<Suggestion>();
        }
        if (string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase) || table.Rows.Count == 0)
        {
            return new List<Suggestion>();
        }

        var filtered = FilteredFields(fetch);
        var narrow = Narrow(entity, table, truncated, filtered);
        var narrowFields = new HashSet<string>(narrow.Select(s => s.Id.Split(':')[1]), StringComparer.OrdinalIgnoreCase);
        var all = new List<Suggestion>(narrow);
        AddIfAny(all, Group(entity, table, filtered, narrowFields));
        AddIfAny(all, Related(fetch, entity, entities));
        AddIfAny(all, Time(entity, filtered));
        AddIfAny(all, Sort(entity, table, truncated, filtered));
        return all.Take(Max).ToList();
    }

    /// <summary>
    /// Returns the fields that already have a condition in the query, including related ones, so they are not suggested again.
    /// </summary>
    public static HashSet<string> FilteredFields(XElement fetch)
    {
        return new HashSet<string>(
            fetch.Descendants("condition").Select(c => (string?)c.Attribute("attribute")).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddIfAny(List<Suggestion> list, Suggestion? suggestion)
    {
        if (suggestion is not null)
        {
            list.Add(suggestion);
        }
    }

    private static List<Suggestion> Narrow(EntityInfo entity, ResultTable table, bool truncated, HashSet<string> filtered)
    {
        var result = new List<Suggestion>();
        var optionSets = entity.Attributes.Where(a => NarrowHintBuilder.Usable(a, entity, filtered) && NarrowHintBuilder.IsOptionSet(a))
            .OrderBy(NarrowHintBuilder.Rank).ToList();
        foreach (var a in optionSets)
        {
            if (result.Count == 2)
            {
                break;
            }
            var column = ColumnIndex(table, a.LogicalName);
            if (column < 0)
            {
                continue;
            }
            var counts = table.Rows.Select(r => column < r.Count ? r[column] : "").Where(v => !string.IsNullOrWhiteSpace(v))
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
            if (counts.Count < 2)
            {
                continue;
            }
            var option = a.Options.FirstOrDefault(o => string.Equals(o.Label, counts[0].Key, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                result.Add(new Suggestion($"narrow:{a.LogicalName}:{option.Value}", "narrow", $"{a.Label}: {option.Label}"));
            }
        }
        if (result.Count == 0 && truncated && optionSets.Count > 0)
        {
            var a = optionSets[0];
            foreach (var option in a.Options.Where(o => !string.IsNullOrWhiteSpace(o.Label)).Take(2))
            {
                result.Add(new Suggestion($"narrow:{a.LogicalName}:{option.Value}", "narrow", $"{a.Label}: {option.Label}"));
            }
        }
        return result;
    }

    private static Suggestion? Group(EntityInfo entity, ResultTable table, HashSet<string> filtered, HashSet<string> narrowFields)
    {
        if (table.Rows.Count < 2)
        {
            return null;
        }
        var candidates = entity.Attributes
            .Where(a => NarrowHintBuilder.Usable(a, entity, filtered) && !narrowFields.Contains(a.LogicalName) && !NotGroupable.Contains(a.LogicalName, StringComparer.OrdinalIgnoreCase)
                && (NarrowHintBuilder.IsOptionSet(a) || IsLookup(a))
                && HasSpread(table, a))
            .OrderBy(a => NarrowHintBuilder.IsOptionSet(a) ? (NarrowHintBuilder.Rank(a) == 1 ? 0 : 2) : 1)
            .ToList();
        var pick = candidates.FirstOrDefault();
        return pick is null ? null : new Suggestion($"group:{pick.LogicalName}", "group", CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.SuggestCountBy, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("field", pick.Label))));
    }

    private static bool HasSpread(ResultTable table, AttributeInfo a)
    {
        var column = ColumnIndex(table, a.LogicalName);
        if (column < 0)
        {
            return NarrowHintBuilder.IsOptionSet(a) && NarrowHintBuilder.Rank(a) != 1;
        }
        return table.Rows.Select(r => column < r.Count ? r[column] : "").Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
    }

    private static Suggestion? Related(XElement fetch, EntityInfo entity, IReadOnlyList<EntitySummary> entities)
    {
        var linked = new HashSet<string>(fetch.Descendants("link-entity").Select(l => (string?)l.Attribute("name") ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var child in CommonRelated)
        {
            var related = entity.Relationships.Any(r => string.Equals(r.ReferencedEntity, entity.LogicalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ReferencingEntity, child, StringComparison.OrdinalIgnoreCase));
            var summary = entities.FirstOrDefault(e => string.Equals(e.LogicalName, child, StringComparison.OrdinalIgnoreCase));
            if (related && summary is not null && !string.IsNullOrWhiteSpace(summary.Label) && !linked.Contains(child)
                && !string.Equals(child, entity.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                return new Suggestion($"related:{child}", "related", CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.SuggestRelated, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("child", summary.Label))),
                    CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.SuggestRelatedQuestion, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("child", summary.Label), ("entity", entity.Label))));
            }
        }
        return null;
    }

    private static Suggestion? Time(EntityInfo entity, HashSet<string> filtered)
    {
        var created = entity.FindAttribute("createdon");
        return created is not null && created.AttributeType == "DateTime" && !filtered.Contains("createdon")
            ? new Suggestion("time:createdon:30d", "time", CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.SuggestRecent)) : null;
    }

    private static Suggestion? Sort(EntityInfo entity, ResultTable table, bool truncated, HashSet<string> filtered)
    {
        if (table.Rows.Count <= 10 && !truncated)
        {
            return null;
        }
        var pick = entity.Attributes.Where(a => a.AttributeType == "Money" && IsSortable(a) && NarrowHintBuilder.Usable(a, entity, filtered)).FirstOrDefault();
        return pick is null ? null : new Suggestion($"sort:{pick.LogicalName}", "sort", CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.SuggestLargest, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("field", pick.Label))));
    }

    /// <summary>
    /// Checks whether an attribute is a lookup.
    /// </summary>
    public static bool IsLookup(AttributeInfo a) => a.AttributeType is "Lookup" or "Customer" or "Owner";

    /// <summary>
    /// Checks whether an attribute can be used for sorting.
    /// </summary>
    public static bool IsSortable(AttributeInfo a)
    {
        var numeric = a.AttributeType is "Money" or "Decimal" or "Double" or "Integer" or "BigInt";
        var n = a.LogicalName;
        return numeric && !n.Contains("longitude", StringComparison.OrdinalIgnoreCase) && !n.Contains("latitude", StringComparison.OrdinalIgnoreCase)
            && !n.Contains("sequence", StringComparison.OrdinalIgnoreCase) && !n.Contains("code", StringComparison.OrdinalIgnoreCase);
    }

    private static int ColumnIndex(ResultTable table, string attribute)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i], attribute, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
