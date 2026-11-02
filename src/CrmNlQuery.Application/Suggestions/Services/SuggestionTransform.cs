using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Suggestions.Services;

/// <summary>
/// Validates a suggestion id against the schema and changes the stored query, with no model.
/// </summary>
public static class SuggestionTransform
{
    /// <summary>
    /// Result of applying a suggestion: the new query and the question suffix.
    /// </summary>
    public sealed record Applied(XDocument Document, string Label);

    /// <summary>
    /// Parses a suggestion id and applies it to the stored query, returning the new query or null if invalid.
    /// </summary>
    public static Applied? Apply(string fetchXml, EntityInfo entity, string suggestionId)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            return null;
        }
        var fetch = doc.Root;
        var scope = fetch?.Element("entity");
        if (fetch is null || scope is null || string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var parts = suggestionId.Split(':');
        if (parts.Length < 2)
        {
            return null;
        }
        var attribute = entity.FindAttribute(parts[1]);
        var filtered = SuggestionBuilder.FilteredFields(fetch);
        if (attribute is null || !NarrowHintBuilder.Usable(attribute, entity, filtered))
        {
            return null;
        }

        switch (parts[0])
        {
            case "narrow" when parts.Length == 3:
                return Narrow(doc, scope, attribute, parts[2]);
            case "group" when parts.Length == 2:
                return Group(fetch, scope, entity, attribute);
            case "sort" when parts.Length == 2:
                return Sort(fetch, scope, attribute);
            case "time" when parts.Length == 3 && parts[1] == "createdon" && parts[2] == "30d":
                AddCondition(scope, new XElement("condition", new XAttribute("attribute", "createdon"), new XAttribute("operator", "last-x-days"), new XAttribute("value", "30")));
                return attribute.AttributeType == "DateTime" ? new Applied(doc, "created in the last 30 days") : null;
            default:
                return null;
        }
    }

    private static Applied? Narrow(XDocument doc, XElement scope, AttributeInfo attribute, string value)
    {
        if (!NarrowHintBuilder.IsOptionSet(attribute) || !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }
        var option = attribute.Options.FirstOrDefault(o => o.Value == number);
        if (option is null)
        {
            return null;
        }
        AddCondition(scope, new XElement("condition", new XAttribute("attribute", attribute.LogicalName), new XAttribute("operator", "eq"),
            new XAttribute("value", number.ToString(CultureInfo.InvariantCulture))));
        return new Applied(doc, $"{attribute.Label}: {option.Label}");
    }

    private static Applied? Group(XElement fetch, XElement scope, EntityInfo entity, AttributeInfo attribute)
    {
        if (!(NarrowHintBuilder.IsOptionSet(attribute) || SuggestionBuilder.IsLookup(attribute)))
        {
            return null;
        }
        foreach (var a in scope.Descendants("attribute").ToList())
        {
            a.Remove();
        }
        foreach (var o in scope.Descendants("order").ToList())
        {
            o.Remove();
        }
        foreach (var name in new[] { "top", "count", "page", "paging-cookie", "returntotalrecordcount", "distinct" })
        {
            fetch.Attribute(name)?.Remove();
        }
        fetch.SetAttributeValue("aggregate", "true");
        scope.AddFirst(
            new XElement("attribute", new XAttribute("name", attribute.LogicalName), new XAttribute("groupby", "true"), new XAttribute("alias", "g")),
            new XElement("attribute", new XAttribute("name", entity.PrimaryIdAttribute), new XAttribute("aggregate", "countcolumn"), new XAttribute("distinct", "true"), new XAttribute("alias", "n")),
            new XElement("order", new XAttribute("alias", "n"), new XAttribute("descending", "true")));
        return new Applied(fetch.Document!, $"count by {attribute.Label}");
    }

    private static Applied? Sort(XElement fetch, XElement scope, AttributeInfo attribute)
    {
        if (!SuggestionBuilder.IsSortable(attribute))
        {
            return null;
        }
        foreach (var o in scope.Elements("order").ToList())
        {
            o.Remove();
        }
        var attributes = scope.Elements("attribute").ToList();
        if (!attributes.Any(a => string.Equals((string?)a.Attribute("name"), attribute.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            var added = new XElement("attribute", new XAttribute("name", attribute.LogicalName));
            if (attributes.Count > 0) { attributes[^1].AddAfterSelf(added); } else { scope.AddFirst(added); }
            attributes = scope.Elements("attribute").ToList();
        }
        var order = new XElement("order", new XAttribute("attribute", attribute.LogicalName), new XAttribute("descending", "true"));
        attributes[^1].AddAfterSelf(order);
        foreach (var name in new[] { "count", "page", "paging-cookie", "returntotalrecordcount" })
        {
            fetch.Attribute(name)?.Remove();
        }
        fetch.SetAttributeValue("top", "10");
        return new Applied(fetch.Document!, $"largest 10 by {attribute.Label}");
    }

    private static void AddCondition(XElement scope, XElement condition)
    {
        var filters = scope.Elements("filter").ToList();
        if (filters.Count == 0)
        {
            var first = new XElement("filter", condition);
            var anchor = scope.Elements().LastOrDefault(e => e.Name.LocalName is "attribute" or "order");
            if (anchor is not null) { anchor.AddAfterSelf(first); } else { scope.AddFirst(first); }
            return;
        }
        if (filters.Count == 1 && !string.Equals((string?)filters[0].Attribute("type"), "or", StringComparison.OrdinalIgnoreCase))
        {
            filters[0].Add(condition);
            return;
        }
        var wrapper = new XElement("filter", new XAttribute("type", "and"));
        filters[0].AddBeforeSelf(wrapper);
        foreach (var f in filters)
        {
            f.Remove();
            wrapper.Add(f);
        }
        wrapper.Add(condition);
    }
}
